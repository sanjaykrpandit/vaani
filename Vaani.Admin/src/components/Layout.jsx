import { useAuth } from '../context/AuthContext'
import { useNavigate } from 'react-router-dom'
import logo from '../Assets/logo.png'

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
              <img src={logo} height="20" alt="Vaani" className="logo-image" /> Admin Console
            </h2>
          </div>
          <div className="header-right">
            <span className="user-info">
              Welcome, {user?.fullName || user?.userId}
            </span>
            <button 
              className="btn btn-secondary btn-sm"
              onClick={handleLogout}
            >
              Logout
            </button>
          </div>
        </div>
      </header>
      
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
