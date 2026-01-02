import { useAuth } from '../context/AuthContext'
import { useNavigate } from 'react-router-dom'

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
            <h2 className="logo">Vaani Admin</h2>
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
