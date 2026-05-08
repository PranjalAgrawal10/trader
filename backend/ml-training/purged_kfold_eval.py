"""
Purged K-Fold (+ embargo) for next-bar logistic baseline — offline evaluation only.

Produces Brier/log-loss aggregates and optional sklearn isotonic calibration JSON
compatible with Trader.Api JsonPiecewiseProbabilityCalibrator (`xs`/`ys`).
"""
from __future__ import annotations

import argparse
import json
from dataclasses import dataclass

import numpy as np
from sklearn.calibration import IsotonicRegression
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import brier_score_loss, log_loss


@dataclass(frozen=True)
class PurgedKFolds:
    """Contiguous chronological folds with purge-before-test-start and embargo-after-test."""

    n_samples: int
    n_splits: int
    label_horizon_bars: int
    embargo_bars: int

    def splits(self):
        edges = np.linspace(0, self.n_samples, self.n_splits + 1, dtype=int)
        for i in range(self.n_splits):
            ts, te = int(edges[i]), int(edges[i + 1])
            if te - ts < 50:
                continue
            purge_lo = max(0, ts - self.label_horizon_bars)
            embargo_hi = min(self.n_samples, te + self.embargo_bars)

            test_mask = np.zeros(self.n_samples, dtype=bool)
            test_mask[ts:te] = True

            forbid = np.zeros(self.n_samples, dtype=bool)
            forbid[ts:te] = True
            forbid[purge_lo:ts] = True
            forbid[te:embargo_hi] = True

            train_idx = np.where(~forbid)[0]
            test_idx = np.where(test_mask)[0]
            yield train_idx, test_idx


def sma(arr: np.ndarray, period: int) -> np.ndarray:
    cum = np.cumsum(np.insert(arr.astype(float), 0, 0.0))
    out = (cum[period:] - cum[:-period]) / period
    pad = np.full(period - 1, np.nan)
    return np.concatenate([pad, out])


def bar_signed_labels(close: np.ndarray, thresh: float) -> np.ndarray:
    rr = np.zeros_like(close, dtype=float)
    rr[1:] = (close[1:] - close[:-1]) / np.maximum(close[:-1], 1e-12)
    signed = np.zeros_like(close, dtype=float)
    signed[rr > thresh] = 1.0
    signed[rr < -thresh] = -1.0
    return signed


def build_feature_matrix(close: np.ndarray, first_ix: int, thresh: float) -> tuple[np.ndarray, np.ndarray]:
    r1 = np.zeros_like(close, dtype=float)
    r1[1:] = (close[1:] - close[:-1]) / np.maximum(close[:-1], 1e-12)

    def ret_n(n: int) -> np.ndarray:
        out = np.zeros_like(close, dtype=float)
        out[n:] = (close[n:] - close[:-n]) / np.maximum(close[:-n], 1e-12)
        return out

    r3, r5 = ret_n(3), ret_n(5)
    sma5 = sma(close, 5)
    sma10 = sma(close, 10)
    gap = np.zeros_like(close, dtype=float)
    ok = (~np.isnan(sma5)) & (~np.isnan(sma10)) & (sma10 > 1e-12)
    gap[ok] = (sma5[ok] - sma10[ok]) / sma10[ok]

    signed = bar_signed_labels(close, thresh)
    feats = []
    ys = []
    for i in range(first_ix, len(close) - 1):
        nxt = signed[i + 1]
        if nxt == 0.0:
            continue
        feats.append([r1[i], r3[i], r5[i], gap[i]])
        ys.append(1 if nxt > 0 else 0)
    return np.asarray(feats, dtype=float), np.asarray(ys, dtype=int)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--n", type=int, default=6_000, help="synthetic bars (ignored with --closes-csv)")
    parser.add_argument("--k", type=int, default=5, help="contiguous folds")
    parser.add_argument("--label-h", type=int, default=1)
    parser.add_argument("--embargo", type=int, default=14, help="match max feature lookback used in training rows")
    parser.add_argument("--thresh", type=float, default=0.0)
    parser.add_argument("--closes-csv", help="CSV with a `close` column (sorted oldest→newest)")
    parser.add_argument("--calibration-out", help="JSON path for isotonic xs/ys on OOF probabilities")
    args = parser.parse_args()

    if args.closes_csv:
        import pandas as pd

        df = pd.read_csv(args.closes_csv)
        close = df["close"].astype(float).to_numpy()
    else:
        rng = np.random.default_rng(42)
        ret = rng.normal(0, 0.0012, size=args.n)
        close = 100 * np.cumprod(1.0 + ret)

    first_ix = 14
    X, y = build_feature_matrix(close, first_ix, args.thresh)

    pk = PurgedKFolds(len(X), args.k, args.label_h, args.embargo)
    probs_oof = np.full(len(X), np.nan)
    for tr_rel, te_rel in pk.splits():
        if len(tr_rel) < 200 or len(te_rel) < 50:
            continue
        clf = LogisticRegression(max_iter=120, solver="lbfgs")
        clf.fit(X[tr_rel], y[tr_rel])
        probs_oof[te_rel] = clf.predict_proba(X[te_rel])[:, 1]

    mask = ~np.isnan(probs_oof)
    y_eval = y[mask]
    p_eval = probs_oof[mask]
    bs = float(brier_score_loss(y_eval, p_eval))
    ll = float(log_loss(y_eval, np.clip(p_eval, 1e-6, 1 - 1e-6)))
    print(f"samples labeled={int(mask.sum())} brier={bs:.6f} log_loss={ll:.6f}")

    if args.calibration_out and mask.sum() > 120:
        iso = IsotonicRegression(out_of_bounds="clip")
        iso.fit(p_eval, y_eval)
        xs = np.linspace(0.0, 1.0, 33)
        ys = iso.predict(xs).astype(float).tolist()
        with open(args.calibration_out, "w", encoding="utf-8") as fh:
            json.dump({"xs": xs.tolist(), "ys": ys}, fh, indent=2)
        print(f"wrote calibration {args.calibration_out}")


if __name__ == "__main__":
    main()
