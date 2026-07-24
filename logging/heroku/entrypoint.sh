#!/bin/bash
set -euo pipefail

# Prefer explicit OPENSEARCH_HOSTS; fall back to Bonsai / Elastic URL config vars.
if [[ -z "${OPENSEARCH_HOSTS:-}" ]]; then
  if [[ -n "${ELASTICSEARCH_HOSTS:-}" ]]; then
    export OPENSEARCH_HOSTS="${ELASTICSEARCH_HOSTS}"
  elif [[ -n "${BONSAI_URL:-}" ]]; then
    export OPENSEARCH_HOSTS="${BONSAI_URL}"
  else
    export OPENSEARCH_HOSTS="http://localhost:9200"
  fi
fi

export DISABLE_SECURITY_DASHBOARDS_PLUGIN="${DISABLE_SECURITY_DASHBOARDS_PLUGIN:-true}"

if [[ -z "${DASHBOARDS_BASIC_AUTH_USER:-}" || -z "${DASHBOARDS_BASIC_AUTH_PASSWORD:-}" ]]; then
  echo "Set DASHBOARDS_BASIC_AUTH_USER and DASHBOARDS_BASIC_AUTH_PASSWORD on the Heroku app."
  exit 1
fi

# Dashboards binds privately; Heroku $PORT is served by the Basic Auth proxy.
export SERVER_HOST="127.0.0.1"
export SERVER_PORT="5601"
export DASHBOARDS_UPSTREAM="http://127.0.0.1:5601"
export PORT="${PORT:-8080}"

_es_host_safe="$(echo "${OPENSEARCH_HOSTS}" | sed -E 's#://[^/@]+@#://***@#')"
echo "Starting OpenSearch Dashboards on ${SERVER_HOST}:${SERVER_PORT} → ${_es_host_safe}"

cd /usr/share/opensearch-dashboards
IDX="${OPENSEARCH_DASHBOARDS_INDEX:-.kibana_heroku_trader}"
NODE_BIN="./node/bin/node"
if [[ ! -x "${NODE_BIN}" ]]; then
  NODE_BIN="$(command -v node || true)"
fi
if [[ -z "${NODE_BIN}" ]]; then
  echo "Node.js binary not found for auth proxy."
  exit 1
fi

# Belt-and-suspenders: remove securityDashboards if an older image still has it.
# Without this, OSD redirects to /app/login after our HTML gate succeeds.
if [[ -d ./plugins/securityDashboards ]]; then
  echo "Removing leftover securityDashboards plugin..."
  ./bin/opensearch-dashboards-plugin remove securityDashboards \
    || rm -rf ./plugins/securityDashboards
fi

# Stock image yml keeps opensearch_security.* keys; OSD fatals if the plugin is gone.
CFG="./config/opensearch_dashboards.yml"
if [[ -f "${CFG}" ]] && grep -q '^opensearch_security\.' "${CFG}"; then
  echo "Stripping opensearch_security.* keys from ${CFG}"
  sed -i '/^opensearch_security\./d' "${CFG}"
fi

./bin/opensearch-dashboards \
  --server.host="${SERVER_HOST}" \
  --server.port="${SERVER_PORT}" \
  --opensearch.hosts="${OPENSEARCH_HOSTS}" \
  --opensearch.ssl.verificationMode=none \
  --opensearchDashboards.index="${IDX}" \
  --migrations.skip=false &
OSD_PID=$!

cleanup() {
  kill "${OSD_PID}" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

# Wait until Dashboards accepts connections (or fail after ~3 minutes).
ready=0
for _ in $(seq 1 90); do
  if "${NODE_BIN}" -e "require('net').connect(5601,'127.0.0.1',()=>process.exit(0)).on('error',()=>process.exit(1))" 2>/dev/null; then
    ready=1
    break
  fi
  if ! kill -0 "${OSD_PID}" 2>/dev/null; then
    echo "OpenSearch Dashboards exited before becoming ready."
    exit 1
  fi
  sleep 2
done
if [[ "${ready}" -ne 1 ]]; then
  echo "Timed out waiting for OpenSearch Dashboards on 127.0.0.1:5601"
  exit 1
fi

echo "Starting Basic Auth proxy (user=${DASHBOARDS_BASIC_AUTH_USER})"
# Foreground (do not exec — keep trap so OSD is cleaned up on exit).
"${NODE_BIN}" /usr/share/opensearch-dashboards/auth-proxy.js
wait "${OSD_PID}"
