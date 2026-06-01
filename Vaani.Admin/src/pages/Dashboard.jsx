import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import Layout from '../components/Layout'
import MeetingList from '../components/MeetingList'
import { meetingService } from '../services/meetingService'
import logo from '../Assets/logo.png'
import analysis from '../Assets/analysis.png'

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
    if (!window.confirm('Are you sure you want to delete this meeting?')) return

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

  const handleViewMetrics = (meetingId) => {
    navigate(`/meetings/${meetingId}/metrics`)
  }

  const handleLaunch = (meetingId) => {
    const origin = window.location.origin
    const targetUrl =
      `http://20.198.120.78/api/launcher/Vaani.application` +
      `?meetingId=${meetingId}&origin=${encodeURIComponent(origin)}`

    const isEdge = /Edg\//.test(navigator.userAgent)
    const launchUrl = isEdge ? targetUrl : `microsoft-edge:${targetUrl}`

    const win = window.open(launchUrl, '_blank')
    if (win) {
      setTimeout(() => window.close(), 1000)
    }
  }

  const handleLaunchVaaniTranslation = (meetingId) => {
    const origin = window.location.origin
    const targetUrl =
      `http://20.198.120.78/api/launcher/translator/VaaniTranslator.application` +
      `?meetingId=${meetingId}&origin=${encodeURIComponent(origin)}`

    const isEdge = /Edg\//.test(navigator.userAgent)
    const launchUrl = isEdge ? targetUrl : `microsoft-edge:${targetUrl}`

    const win = window.open(launchUrl, '_blank')
    if (win) {
      setTimeout(() => window.close(), 1000)
    }
  }

  /* 🔹 Status helpers */
  const isUpcoming = (m) => new Date() < new Date(m.validFrom)
  const isRunning = (m) =>
    new Date() >= new Date(m.validFrom) &&
    new Date() <= new Date(m.validUntil)
  const isCompleted = (m) => new Date() > new Date(m.validUntil)

  /* 🔹 Filter logic */
  const getFilteredMeetings = () => {
    switch (filter) {
      case 'upcoming':
        return meetings
          .filter(isUpcoming)
          .sort((a, b) => new Date(a.validFrom) - new Date(b.validFrom))

      case 'running':
        return meetings
          .filter(isRunning)
          .sort((a, b) => new Date(a.validUntil) - new Date(b.validUntil))

      case 'completed':
        return meetings
          .filter(isCompleted)
          .sort((a, b) => new Date(b.validUntil) - new Date(a.validUntil))

      default:
        return [...meetings].sort(
          (a, b) => new Date(b.validUntil) - new Date(a.validUntil)
        )
    }
  }

  const filteredMeetings = getFilteredMeetings()

  return (
    <Layout>
      <div className="dashboard">
        <div className="dashboard-header">
          <h1 className='h1-header'>Meetings</h1>
          <button
            className="btn btn-sm btn-primary"
            onClick={() => navigate('/meetings/add')}
          >
            + Add Meeting
          </button>
        </div>

        {/* 🔹 FILTER BAR */}
        <div className="filter-bar">
          <button
            className={`filter-btn ${filter === 'all' ? 'active' : ''}`}
            onClick={() => setFilter('all')}
          >
            All ({meetings.length})
          </button>

          <button
            className={`filter-btn ${filter === 'upcoming' ? 'active' : ''}`}
            onClick={() => setFilter('upcoming')}
          >
            Upcoming ({meetings.filter(isUpcoming).length})
          </button>

          <button
            className={`filter-btn ${filter === 'running' ? 'active' : ''}`}
            onClick={() => setFilter('running')}
          >
            Running ({meetings.filter(isRunning).length})
          </button>

          <button
            className={`filter-btn ${filter === 'completed' ? 'active' : ''}`}
            onClick={() => setFilter('completed')}
          >
            Completed ({meetings.filter(isCompleted).length})
          </button>
        </div>

        {error && (
          <div className="error-message">
            {error}
            <button onClick={loadMeetings} className="btn-link">
              Retry
            </button>
          </div>
        )}

        {loading ? (
          <div className="loading">Loading meetings...</div>
        ) : (
          <MeetingList
            analysis={analysis}
            logo={logo}
            meetings={filteredMeetings}
            onLaunch={handleLaunch}
            onLaunchVaaniTranslation={handleLaunchVaaniTranslation}
            onEdit={handleEdit}
            onDelete={handleDelete}
            onViewMetrics={handleViewMetrics}
          />
        )}
      </div>
    </Layout>
  )
}

export default Dashboard
