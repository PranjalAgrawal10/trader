import type { ReactNode } from 'react'
import { Button, Container, Nav, Navbar } from 'react-bootstrap'
import { Link, NavLink, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../store/useAuthStore'

export function Layout({ children }: { children: ReactNode }) {
  const navigate = useNavigate()
  const { logout } = useAuthStore()

  const handleLogout = () => {
    logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="min-vh-100 d-flex flex-column bg-body-tertiary">
      <Navbar expand="lg" bg="dark" variant="dark" className="border-bottom border-secondary">
        <Container>
          <Navbar.Brand as={Link} to="/" className="text-success">
            Trader
          </Navbar.Brand>
          <Navbar.Toggle aria-controls="main-nav" />
          <Navbar.Collapse id="main-nav">
            <Nav className="me-auto mb-3 mb-lg-0">
              <Nav.Link as={NavLink} to="/" end>
                Dashboard
              </Nav.Link>
              <Nav.Link as={NavLink} to="/instruments">
                F&amp;O / MCX
              </Nav.Link>
              <Nav.Link as={NavLink} to="/strategies">
                Strategies
              </Nav.Link>
              <Nav.Link as={NavLink} to="/bots">
                Bots
              </Nav.Link>
              <Nav.Link as={NavLink} to="/trades">
                Trades
              </Nav.Link>
            </Nav>
            <Nav className="ms-lg-auto align-items-lg-center gap-2 flex-column flex-lg-row">
              <Nav.Link as={NavLink} to="/profile">
                Profile
              </Nav.Link>
              <Button variant="outline-light" size="sm" className="w-100 w-lg-auto" onClick={handleLogout}>
                Sign out
              </Button>
            </Nav>
          </Navbar.Collapse>
        </Container>
      </Navbar>
      <Container className="py-4 flex-grow-1">{children}</Container>
    </div>
  )
}
