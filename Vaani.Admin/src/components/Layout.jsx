import { useAuth } from '../context/AuthContext'
import { useNavigate } from 'react-router-dom'
import logo from '../Assets/logo.png'
import './Layout.css'

function Layout({ children }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  return (
    <div className="layout">
      <header className="header">
        <div className="header-content">
          <div className="header-left">
            <h2 className="logo">
              <img
                src={logo}
                height="12"
                alt="Vaani"
                className="logo-image"
              />{' '}
              Admin Console
            </h2>
          </div>

          <div className="header-right">
            <span className="user-info">
              Welcome, {user?.fullName || user?.userId}
            </span>
            <button
              className="btn btn-secondary btn-xsm"
              onClick={handleLogout}
            >
              Logout
            </button>
          </div>
        </div>
      </header>

      {/* ✅ FIXED: div instead of p */}
      <div className="nav-content">
        <nav className="nav-menu">
          <button
            className="btn btn-xsm"
            onClick={() => navigate('/dashboard')}
          >
            📊 Dashboard
          </button>

          <button
            className="btn btn-xsm"
            onClick={() => navigate('/languages')}
          >
            🌐 Languages
          </button>

          <button
            className="btn btn-xsm"
            onClick={() => navigate('/meetings')}
          >
            📞 Meetings
          </button>

          <button
            className="btn btn-xsm"
            onClick={() => navigate('/users')}
          >
            👥 Users
          </button>

          <button
            className="btn btn-xsm"
            onClick={() => navigate('/dictionary')}
          >
            📖 Dictionary
          </button>
        </nav>
      </div>

      <main className="main-content">
        {children}
      </main>

      <footer className="footer">
        <p>&copy; 2026 Vaani Admin Console. All rights reserved.</p>
      </footer>
    </div>
  )
}

export default Layout
