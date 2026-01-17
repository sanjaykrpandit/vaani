import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import './MeetingDownload.css'

function MeetingDownload() {
  const navigate = useNavigate()
  const [meetingAccess, setMeetingAccess] = useState(null)
  const [launching, setLaunching] = useState(false)

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

  const handleLaunch = () => {
    if (!meetingAccess) return

    setLaunching(true)

    try {
      const origin = window.location.origin
      
      // Use downloadLink from backend (appsettings.json) or fallback to hardcoded
      const baseUrl = meetingAccess.downloadLink || 
                     'https://vaani-rtt-api.tryzent.com/launcher/Vaani.application'
      
      const targetUrl = `${baseUrl}?meetingId=${meetingAccess.meetingId}&origin=${encodeURIComponent(origin)}`

      const isEdge = /Edg\//.test(navigator.userAgent)
      const launchUrl = isEdge ? targetUrl : `microsoft-edge:${targetUrl}`

      // Open the application
      const win = window.open(launchUrl, '_blank')
      
      if (win) {
        // Give user feedback
        setTimeout(() => {
          setLaunching(false)
          alert('Application launched successfully! You can now close this window.')
        }, 2000)
      } else {
        setLaunching(false)
        alert('Pop-up blocked. Please allow pop-ups for this site and try again.')
      }
    } catch (error) {
      console.error('Error launching application:', error)
      setLaunching(false)
      alert('Failed to launch application. Please try again.')
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
          <h1>🎯 Ready to Join</h1>
          <p>Your meeting is ready to launch</p>
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
            
            <div className="info-row">
              <span className="info-label">Language:</span>
              <span className="info-value">{meetingAccess.meetingLanguage}</span>
            </div>
          </div>

          <div className="launch-section">
            <button
              className="btn btn-launch"
              onClick={handleLaunch}
              disabled={launching}
            >
              {launching ? '🚀 Launching...' : '🚀 Launch Application'}
            </button>

            <div className="instructions">
              <h3>Before you launch:</h3>
              <ul>
                <li>✓ Make sure you're using Microsoft Edge browser</li>
                <li>✓ Allow pop-ups if prompted</li>
                <li>✓ The desktop application will download and launch automatically</li>
                <li>✓ Follow any installation prompts if this is your first time</li>
              </ul>
            </div>

            <div className="help-section">
              <p className="help-text">
                <strong>Need help?</strong> If the application doesn't launch, please ensure:
              </p>
              <ul className="troubleshooting">
                <li>You're using Microsoft Edge browser</li>
                <li>Pop-ups are enabled for this site</li>
                <li>ClickOnce applications are allowed in your browser settings</li>
              </ul>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

export default MeetingDownload
