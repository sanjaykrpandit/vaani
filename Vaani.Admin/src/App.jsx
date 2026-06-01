import { Routes, Route, Navigate } from 'react-router-dom'
import { useAuth } from './context/AuthContext'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import AddMeeting from './pages/AddMeeting'
import EditMeeting from './pages/EditMeeting'
import MeetingMetricsPage from './pages/MeetingMetricsPage'
import PublicMeetingAccess from './pages/PublicMeetingAccess'
import MeetingDownload from './pages/MeetingDownload'
import Thanks from './pages/Thanks'
import ProtectedRoute from './components/ProtectedRoute'
import LanguageList from './components/LanguageList'
import UserList from './components/UserList'
import DictionaryList from './components/DictionaryList'
import './App.css'

function App() {
  const { isAuthenticated } = useAuth()

  return (
    <Routes>
      {/* Public routes - no authentication required */}
      <Route path="/public-access" element={<PublicMeetingAccess />} />
      <Route path="/meeting-download" element={<MeetingDownload />} />
      <Route path="/thanks" element={<Thanks />} />

      {/* Authentication route */}
      <Route
        path="/login"
        element={isAuthenticated ? <Navigate to="/dashboard" replace /> : <Login />}
      />

      {/* Protected admin routes */}
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
          <ProtectedRoute allowedRoles={['admin']}>
            <EditMeeting />
          </ProtectedRoute>
        }
      />
      <Route
        path="/meetings/:meetingId/metrics"
        element={
          <ProtectedRoute allowedRoles={['admin']}>
            <MeetingMetricsPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/languages"
        element={
          <ProtectedRoute allowedRoles={['admin']}>
            <LanguageList />
          </ProtectedRoute>
        }
      />
      <Route
        path="/users"
        element={
          <ProtectedRoute allowedRoles={['admin']}>
            <UserList />
          </ProtectedRoute>
        }
      />
      <Route
        path="/dictionary"
        element={
          <ProtectedRoute>
            <DictionaryList />
          </ProtectedRoute>
        }
      />
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}

export default App
