import { Routes, Route, Navigate } from 'react-router-dom'
import { useAuth } from './context/AuthContext'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import AddMeeting from './pages/AddMeeting'
import EditMeeting from './pages/EditMeeting'
import MeetingMetricsPage from './pages/MeetingMetricsPage'
import ProtectedRoute from './components/ProtectedRoute'
import LanguageList from './components/LanguageList'
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
      <Route
        path="/meetings/:meetingId/metrics"
        element={
          <ProtectedRoute>
            <MeetingMetricsPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/languages"
        element={
          <ProtectedRoute>
            <LanguageList />
          </ProtectedRoute>
        }
      />
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}

export default App
