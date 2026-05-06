import { useCallback, useEffect, useState } from 'react'
import { Alert, Card, Col, Row, Spinner } from 'react-bootstrap'
import { useSearchParams } from 'react-router-dom'
import { BrokerSettingsSection } from '../components/BrokerSettingsSection'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'
import { Layout } from '../components/Layout'
import { SecuritySettingsSection } from '../components/SecuritySettingsSection'
import { api } from '../api/client'
import { useAuthStore } from '../store/useAuthStore'

type ProfileMe = {
  user_id: string
  email: string
  role: string
  created_at: string
}

function formatJoined(iso: string): string {
  try {
    const d = new Date(iso)
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
  } catch {
    return iso
  }
}

export function ProfilePage() {
  const [searchParams] = useSearchParams()
  const setupRequired = searchParams.get('required') === '1'
  const brokerSetupRequired = searchParams.get('setup') === '1'
  const storeEmail = useAuthStore((s) => s.email)

  const [twoFaEpoch, setTwoFaEpoch] = useState(0)

  const onTwoFactorStatusUpdated = useCallback(() => {
    setTwoFaEpoch((n) => n + 1)
  }, [])

  const [profile, setProfile] = useState<ProfileMe | null>(null)
  const [profileError, setProfileError] = useState<string | null>(null)
  const [profileLoading, setProfileLoading] = useState(true)

  const loadProfile = useCallback(async () => {
    setProfileError(null)
    setProfileLoading(true)
    try {
      const { data } = await api.get<ProfileMe>('/auth/me')
      setProfile(data)
    } catch {
      setProfile(null)
      setProfileError('Could not load account details.')
    } finally {
      setProfileLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadProfile()
  }, [loadProfile])

  useEffect(() => {
    const wantScroll =
      brokerSetupRequired || window.location.hash === `#${BROKER_PROFILE_SECTION_ID}`
    if (!wantScroll) return
    const t = window.setTimeout(() => {
      document.getElementById(BROKER_PROFILE_SECTION_ID)?.scrollIntoView({ behavior: 'smooth' })
    }, 150)
    return () => window.clearTimeout(t)
  }, [brokerSetupRequired, searchParams])

  const displayEmail = profile?.email ?? storeEmail ?? '—'
  const displayRole = profile?.role ?? '—'
  const displayUserId = profile?.user_id ?? '—'
  const displayJoined = profile?.created_at ? formatJoined(profile.created_at) : '—'

  return (
    <Layout>
      <Row className="justify-content-center">
        <Col xs={12} md={10} lg={8}>
          <h1 className="h4 mb-3">Profile</h1>

          {profileError ? (
            <Alert variant="warning" className="mb-3">
              {profileError}{' '}
              <span className="text-secondary">Signed in as {storeEmail ?? 'unknown'}.</span>
            </Alert>
          ) : null}

          <Card className="border-secondary shadow-sm mb-4">
            <Card.Body>
              <Card.Title className="h6">Account</Card.Title>
              {profileLoading ? (
                <div className="d-flex align-items-center gap-2 text-secondary">
                  <Spinner animation="border" size="sm" />
                  Loading account…
                </div>
              ) : (
                <>
                  <dl className="row mb-0 small">
                    <dt className="col-sm-3 text-secondary">Email</dt>
                    <dd className="col-sm-9 font-monospace">{displayEmail}</dd>
                    <dt className="col-sm-3 text-secondary">Role</dt>
                    <dd className="col-sm-9">{displayRole}</dd>
                    <dt className="col-sm-3 text-secondary">User ID</dt>
                    <dd className="col-sm-9 font-monospace text-break">{displayUserId}</dd>
                    <dt className="col-sm-3 text-secondary">Member since</dt>
                    <dd className="col-sm-9">{displayJoined}</dd>
                  </dl>
                </>
              )}
            </Card.Body>
          </Card>

          <h2 className="h5 mb-3">Security</h2>
          <SecuritySettingsSection setupRequired={setupRequired} onStatusUpdated={onTwoFactorStatusUpdated} />

          <h2 className="h5 mt-4 mb-3">Broker connection</h2>
          <BrokerSettingsSection brokerSetupRequired={brokerSetupRequired} twoFaEpoch={twoFaEpoch} />
        </Col>
      </Row>
    </Layout>
  )
}
