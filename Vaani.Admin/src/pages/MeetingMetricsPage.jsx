import { useState, useEffect } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import Layout from '../components/Layout'
import MeetingMetrics from '../components/MeetingMetrics'
import { analyticsService } from '../services/analyticsService'
import { meetingService } from '../services/meetingService'
import './MeetingMetricsPage.css'
import exportIcon from '../Assets/download.png'
import refreshIcon from '../Assets/reload.png'
import backIcon from '../Assets/back.png'

function MeetingMetricsPage() {
  const { meetingId } = useParams()
  const navigate = useNavigate()
  const [meeting, setMeeting] = useState(null)
  const [metrics, setMetrics] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [refreshInterval, setRefreshInterval] = useState(30000) // 30 seconds default
  const [autoRefresh, setAutoRefresh] = useState(true)

  useEffect(() => {
    loadData()
  }, [meetingId])

  useEffect(() => {
    if (!autoRefresh) return

    const interval = setInterval(() => {
      loadMetrics()
    }, refreshInterval)

    return () => clearInterval(interval)
  }, [autoRefresh, refreshInterval, meetingId])

  const loadData = async () => {
    try {
      setLoading(true)
      setError('')
      await Promise.all([loadMeeting(), loadMetrics()])
    } catch (err) {
      setError('Failed to load data')
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  const loadMeeting = async () => {
    try {
      const data = await meetingService.getMeetingById(meetingId)
      setMeeting(data)
    } catch (err) {
      console.error('Failed to load meeting:', err)
    }
  }

  const loadMetrics = async () => {
    try {
      const data = await analyticsService.getSessionMetrics(meetingId)
      setMetrics(data)
    } catch (err) {
      console.error('Failed to load metrics:', err)
      if (!metrics) {
        setError('Failed to load metrics')
      }
    }
  }

  const handleRefresh = () => {
    loadData()
  }

  const handleExport = () => {
    if (!metrics) return

    const exportData = {
      meeting: {
        id: meeting.meetingId,
        name: meeting.meetingName,
        validFrom: meeting.validFrom,
        validUntil: meeting.validUntil
      },
      metrics: {
        totalSessions: metrics.totalSessions,
        activeSessions: metrics.activeSessions,
        endedSessions: metrics.endedSessions,
        expiredSessions: metrics.expiredSessions,
        averageSessionDurationMinutes: metrics.averageSessionDurationMinutes,
        totalHeartbeats: metrics.totalHeartbeats,
        firstSessionStarted: metrics.firstSessionStarted,
        lastSessionActivity: metrics.lastSessionActivity
      },
      devices: metrics.deviceBreakdown,
      recentSessions: metrics.recentSessions
    }

    const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `metrics-${meetingId}-${new Date().toISOString().split('T')[0]}.json`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }

  return (
    <Layout>
      <div className="metrics-page">
        <div className="metrics-header">
          <div className="header-left">
            <button className="btn-back" onClick={() => navigate('/dashboard')}>
               <img src={backIcon} alt="Back" width={24} className="icon" />
            </button>
            <div className="meeting-info">
              <h1>Session Analytics</h1>
              {meeting && (
                <div className="meeting-details">
                  <span className="meeting-name">{meeting.meetingName}</span>
                  <span className="meeting-id">ID: {meeting.meetingId}</span>
                </div>
              )}
            </div>
          </div>
          
          <div className="header-actions">
            <div className="refresh-controls">
              <label className="auto-refresh-toggle">
                <input
                  type="checkbox"
                  checked={autoRefresh}
                  onChange={(e) => setAutoRefresh(e.target.checked)}
                />
                <span>Auto-refresh</span>
              </label>
              {autoRefresh && (
                <select
                  value={refreshInterval}
                  onChange={(e) => setRefreshInterval(Number(e.target.value))}
                  className="refresh-interval"
                >
                  <option value={10000}>10s</option>
                  <option value={30000}>30s</option>
                  <option value={60000}>1m</option>
                  <option value={300000}>5m</option>
                </select>
              )}
            </div>
            <button className="btn btn-gray" onClick={handleRefresh}>
               <img src={refreshIcon} width={22} alt="Refresh" className="icon" />
            </button>
            <button className="btn btn-gray" onClick={handleExport}>
              <img src={exportIcon} width={22} alt="Export" className="icon" />
            </button>
          </div>
        </div>

        {meeting && (
          <div className="meeting-status-bar">
            <div className="status-item">
              <strong>Valid From:</strong>
              <span>{new Date(meeting.validFrom).toLocaleString()}</span>
            </div>
            <div className="status-item">
              <strong>Valid Until:</strong>
              <span>{new Date(meeting.validUntil).toLocaleString()}</span>
            </div>
            <div className="status-item">
              <strong>Status:</strong>
              <span className={`status-badge ${meeting.isActive ? 'active' : 'inactive'}`}>
                {meeting.isActive ? 'Active' : 'Inactive'}
              </span>
            </div>
            <div className="status-item">
              <strong>Azure Region:</strong>
              <span>{meeting.azureSubscription?.region || 'N/A'}</span>
            </div>
          </div>
        )}

        {error && (
          <div className="error-message">
            {error}
            <button onClick={handleRefresh} className="btn-link">Retry</button>
          </div>
        )}

        {loading && !metrics ? (
          <div className="loading">
            <div className="spinner"></div>
            <p>Loading metrics...</p>
          </div>
        ) : (
          <MeetingMetrics metrics={metrics} />
        )}
      </div>
    </Layout>
  )
}

export default MeetingMetricsPage
