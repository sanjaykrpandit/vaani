import { Routes, Route, Navigate } from 'react-router-dom'
import { useAuth } from './context/AuthContext'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import AddMeeting from './pages/AddMeeting'
import EditMeeting from './pages/EditMeeting'
import ProtectedRoute from './components/ProtectedRoute'
import './App.css'

function App() {
  const { isAuthenticated } = useAuth()

  return (
    <Routes>
      <Route 
        path="/login" 
        element={isAuthenticated ? <Navigate to="/dashboard" replace /> : <Login />} 
      />
      <Route
        path="/dashboard"
        element={
          <ProtectedRoute>
            <Dashboard />
          </ProtectedRoute>
        }
      />
      <Route
        path="/meetings/add"
        element={
          <ProtectedRoute>
            <AddMeeting />
          </ProtectedRoute>
        }
      />
      <Route
        path="/meetings/edit/:meetingId"
        element={
          <ProtectedRoute>
            <EditMeeting />
          </ProtectedRoute>
        }
      />
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}

export default App
