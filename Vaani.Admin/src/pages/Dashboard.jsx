import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import Layout from '../components/Layout'
import MeetingList from '../components/MeetingList'
import { meetingService } from '../services/meetingService'

function Dashboard() {
  const [meetings, setMeetings] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [filter, setFilter] = useState('all')
  const navigate = useNavigate()

  useEffect(() => {
    loadMeetings()
  }, [])

  const loadMeetings = async () => {
    try {
      setLoading(true)
      setError('')
      const data = await meetingService.getAllMeetings()
      setMeetings(data)
    } catch (err) {
      setError('Failed to load meetings')
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  const handleDelete = async (meetingId) => {
    if (!window.confirm('Are you sure you want to delete this meeting?')) {
      return
    }

    try {
      await meetingService.deleteMeeting(meetingId)
      setMeetings(meetings.filter(m => m.meetingId !== meetingId))
    } catch (err) {
      alert('Failed to delete meeting')
      console.error(err)
    }
  }

  const handleEdit = (meetingId) => {
    navigate(`/meetings/edit/${meetingId}`)
  }

 
  
const handleLaunch = (meetingId) => {

// const popup = window.open(
//     "/app/index.html",
//     "vaani-launcher",
//     "width=420,height=260,menubar=no,toolbar=no,status=no,resizable=no"
//   );

//   if (!popup || popup.closed || typeof popup.closed === "undefined") {
//     alert("Popup blocked. Please allow popups to launch Vaani.");
//   }

  const origin = window.location.origin
  const targetUrl = `https://vaani-rtt-api.tryzent.com/launcher/Vaani.application?meetingId=${meetingId}&origin=${encodeURIComponent(origin)}`
  const isEdge = /Edg\//.test(navigator.userAgent)
  let launchUrl
  if (isEdge) {
    launchUrl = targetUrl
  } else {
    // Not Edge → force Edge
    launchUrl = `microsoft-edge:${targetUrl}`
  }
  const win = window.open(launchUrl, '_blank')
  // Close current window after launch (may be blocked if not user-initiated)
  if (win) {
    setTimeout(() => {
      window.close()
    }, 1000)
  }
}










  const getFilteredMeetings = () => {
    const now = new Date()
    
    switch (filter) {
      case 'upcoming':
        return meetings
          .filter(m => new Date(m.validUntil) > now)
          .sort((a, b) => new Date(a.validUntil) - new Date(b.validUntil))
      case 'completed':
        return meetings
          .filter(m => new Date(m.validUntil) <= now)
          .sort((a, b) => new Date(b.validUntil) - new Date(a.validUntil))
      default:
        return meetings.sort((a, b) => new Date(b.validUntil) - new Date(a.validUntil))
    }
  }

  const filteredMeetings = getFilteredMeetings()

  return (
    <Layout>
      <div className="dashboard">
        <div className="dashboard-header">
          <h1>Meeting Management</h1>
          <button 
            className="btn btn-primary"
            onClick={() => navigate('/meetings/add')}
          >
            + Add Meeting
          </button>
        </div>

        <div className="filter-bar">
          <button 
            className={`filter-btn ${filter === 'all' ? 'active' : ''}`}
            onClick={() => setFilter('all')}
          >
            All Meetings ({meetings.length})
          </button>
          <button 
            className={`filter-btn ${filter === 'upcoming' ? 'active' : ''}`}
            onClick={() => setFilter('upcoming')}
          >
            Upcoming ({meetings.filter(m => new Date(m.validUntil) > new Date()).length})
          </button>
          <button 
            className={`filter-btn ${filter === 'completed' ? 'active' : ''}`}
            onClick={() => setFilter('completed')}
          >
            Completed ({meetings.filter(m => new Date(m.validUntil) <= new Date()).length})
          </button>
        </div>

        {error && (
          <div className="error-message">
            {error}
            <button onClick={loadMeetings} className="btn-link">Retry</button>
          </div>
        )}

        {loading ? (
          <div className="loading">Loading meetings...</div>
        ) : (
          <MeetingList 
            meetings={filteredMeetings}
            onLaunch={handleLaunch}
            onEdit={handleEdit}
            onDelete={handleDelete}
          />
        )}
      </div>
    </Layout>
  )
}

export default Dashboard
