import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import './MeetingDownload.css'
import logo from '../assets/logo.png'

function MeetingDownload() {
  const navigate = useNavigate()
  const [meetingAccess, setMeetingAccess] = useState(null)
  const [launching, setLaunching] = useState(false)


  const formatDate = (dateString) => {
    const date = new Date(dateString)
    return date.toLocaleString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    })
  }

  useEffect(() => {
    // Check if user has valid access
    const accessData = sessionStorage.getItem('meetingAccess')

    if (!accessData) {
      // No access token, redirect to public access page
      navigate('/public-access')
      return
    }

    try {
      const parsed = JSON.parse(accessData)
      setMeetingAccess(parsed)
    } catch (error) {
      console.error('Invalid access data')
      navigate('/public-access')
    }
  }, [navigate])

  useEffect(() => {
    const timeout = setTimeout(() => {
      sessionStorage.clear()
      navigate('/thanks', { replace: true })
    }, 30000) // 30 seconds

    return () => clearTimeout(timeout)
  }, [navigate])


  const handleLaunch = () => {
    if (!meetingAccess) return

    setLaunching(true)

    try {
      const origin = window.location.origin
      const baseUrl = meetingAccess.downloadLink
      const targetUrl = `${baseUrl}?meetingId=${meetingAccess.meetingId}&origin=${encodeURIComponent(origin)}`
      const isEdge = /Edg\//.test(navigator.userAgent)
      const launchUrl = isEdge ? targetUrl : `microsoft-edge:${targetUrl}`
      window.open(launchUrl, '_blank')
      sessionStorage.removeItem('meetingAccess')
      navigate('/thanks', { replace: true })
    } catch (e) {
      console.error(e)
    } finally {
      setLaunching(false)
    }
  }


  if (!meetingAccess) {
    return (
      <div className="download-container">
        <div className="download-card">
          <p>Loading...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="download-container">
      <div className="download-card">
        <div className="card-header">
          <h2>Your translator is ready to launch</h2>
        </div>

        <div className="card-body">
          <div className="meeting-info">
            <div className="info-row">
              <span className="info-label">Meeting Name:</span>
              <span className="info-value">{meetingAccess.meetingName}</span>
            </div>

            <div className="info-row">
              <span className="info-label">Meeting ID:</span>
              <span className="info-value">{meetingAccess.meetingId}</span>
            </div>

            {<div className="info-row">
              <span className="info-label">Valid Until:</span>
              <span className="info-value">{formatDate(meetingAccess.validUntil)}</span>
            </div>}

          </div>

          <div className="launch-section">
            <button
              className="btn btn-launch"
              onClick={handleLaunch}
              disabled={launching}
            >
              <img src={logo} height={14} alt="Logo" className="logo-icon" />
              {launching ? 'Launching...' : 'Launch Application'}
            </button>

            <div className="instructions">
              <h3>Before you launch:</h3>
              <ul>
                <li>✓ Allow pop-ups if prompted</li>
                <li>✓ The desktop application will download and launch automatically</li>
                <li>✓ Follow any installation prompts if this is your first time</li>
              </ul>
            </div>

            {/* <div className="help-section">
              <p className="help-text">
                <strong>Need help?</strong> If the application doesn't launch, please ensure:
              </p>
              <ul className="troubleshooting">
                <li>You're using Microsoft Edge browser</li>
                <li>Pop-ups are enabled for this site</li>
                <li>ClickOnce applications are allowed in your browser settings</li>
              </ul>
            </div> */}
          </div>
        </div>
      </div>
    </div>
  )
}

export default MeetingDownload
