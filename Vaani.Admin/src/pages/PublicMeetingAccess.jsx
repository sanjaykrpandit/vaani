import { useState, useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { validateMeetingToken } from '../services/tokenService'
import './PublicMeetingAccess.css'

function PublicMeetingAccess() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()

  const [meetingId, setMeetingId] = useState('')
  const [token, setToken] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [meetingDetails, setMeetingDetails] = useState(null)

  useEffect(() => {
    // Read token from URL query parameter (hidden from user)
    const urlToken = searchParams.get('token')

    if (urlToken) {
      setToken(urlToken)
    } else {
      setError('Invalid meeting link. Token is missing.')
    }
  }, [searchParams])

  const handleValidate = async () => {
    if (!meetingId) {
      setError('Please enter your Teams Meeting ID')
      return
    }

    if (!token) {
      setError('Invalid meeting link. Token is missing.')
      return
    }

    setLoading(true)
    setError('')

    try {
      // Validate the meeting token (backend decrypts and validates)
      const data = await validateMeetingToken(meetingId, token)

      // Check if meeting is valid
      if (!data.isValid) {
        setError('Meeting validation failed. Please check your meeting ID.')
        setLoading(false)
        return
      }

      // Store access information in sessionStorage
      sessionStorage.setItem('meetingAccess', JSON.stringify({
        meetingId: data.meetingId,
        meetingName: data.meetingName,
        validUntil: data.validUntil,
        downloadLink: data.downloadLink,
        accessToken: token,
        validatedAt: new Date().toISOString()
      }))

      // Redirect to download page
      navigate('/meeting-download')

    } catch (error) {
      const errorMessage = error.response?.data?.message ||
        error.response?.data?.error ||
        'Invalid or expired meeting link. Please contact the meeting organizer.'
      setError(errorMessage)
      setLoading(false)
    }
  }

  const handleKeyPress = (e) => {
    if (e.key === 'Enter') {
      handleValidate()
    }
  }

  return (
    <div className="public-access-container">
      <div className="public-access-card">
        <div className="card-header">
          <h1>Join Vaani a Realtime voice translator</h1>
          <p>Enter your teams meeting details to join</p>
        </div>

        <div className="card-body">
          <div className="form-group">
            <label htmlFor="meetingId">Teams Meeting ID</label>
            <input
              id="meetingId"
              type="text"
              className="form-control"
              value={meetingId}
              onChange={(e) => setMeetingId(e.target.value)}
              onKeyPress={handleKeyPress}
              placeholder="Enter your Teams Meeting ID"
              disabled={loading}
              autoFocus
            />
          </div>

          {error && (
            <div className="alert alert-error">
              {error}
            </div>
          )}

          <button
            className="btn btn-primary btn-block"
            onClick={handleValidate}
            disabled={loading || !meetingId || !token}
          >
            {loading ? 'Validating...' : 'Join Meeting'}
          </button>

          <div className="help-text">
            <p>
              Don't have a meeting link? Contact your meeting organizer to get access.
            </p>
          </div>
        </div>
      </div>
    </div>
  )
}

export default PublicMeetingAccess
