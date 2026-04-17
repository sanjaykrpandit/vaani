import { useState, useEffect, useCallback } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { validateMeetingToken } from '../services/tokenService'
import './PublicMeetingAccess.css'

function PublicMeetingAccess() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()

  // State Management
  const [meetingId, setMeetingId] = useState('')
  const [token, setToken] = useState('')
  const [loading, setLoading] = useState(false)
  const [isAutoValidating, setIsAutoValidating] = useState(false) // Controls "Hidden" state
  const [error, setError] = useState('')

  /**
   * Core Validation Logic
   * Accepts direct arguments to handle the async nature of React state
   */
  const handleValidate = useCallback(async (idToUse, tokenToUse) => {
    // Fallback to state if arguments aren't provided (for manual button clicks)
    const mId = idToUse || meetingId
    const mToken = tokenToUse || token

    if (!mId) {
      setError('Please enter your Meeting ID')
      setIsAutoValidating(false)
      return
    }

    setLoading(true)
    setError('')

    try {
      // Pass the token as both the ID and the Token as requested
      const data = await validateMeetingToken(mId, mToken)

      if (!data.isValid) {
        throw new Error('Meeting validation failed. Please check your meeting ID.')
      }

      // Success: Store and Redirect
      sessionStorage.setItem('meetingAccess', JSON.stringify({
        meetingId: data.meetingId,
        meetingName: data.meetingName,
        validUntil: data.validUntil,
        downloadLink: data.downloadLink,
        accessToken: mToken,
        validatedAt: new Date().toISOString()
      }))

      navigate('/meeting-download')
    } catch (err) {
      const errorMessage = err.response?.data?.message || 
                         err.message || 
                         'Invalid or expired meeting link.'
      setError(errorMessage)
      setLoading(false)
      setIsAutoValidating(false) // "Unhide" the form so user can see error/fix ID
    }
  }, [meetingId, token, navigate])

  /**
   * Auto-run on Page Load
   */
  useEffect(() => {
    const urlToken = searchParams.get('token')

    if (urlToken) {
      // 1. Pre-fill states for the UI
      setToken(urlToken)
      setMeetingId(urlToken) 
      
      // 2. Hide the form immediately
      setIsAutoValidating(true)

      // 3. Trigger validation using the token for BOTH parameters
      handleValidate(urlToken, urlToken)
    } else {
      // No token in URL? Just show the empty form
      setIsAutoValidating(false)
    }
  }, [searchParams, handleValidate])

  const handleKeyPress = (e) => {
    if (e.key === 'Enter') handleValidate()
  }

  // SCREEN 1: Loading/Hidden State (shown during auto-validation)
  if (isAutoValidating) {
    return (
      <div className="public-access-container">
        <div className="loader-container">
          <div className="spinner"></div>
          <p>Verifying meeting access...</p>
        </div>
      </div>
    )
  }

  // SCREEN 2: The Form (shown if no token present or if validation fails)
  return (
    <div className="public-access-container">
      <div className="public-access-card">
        <div className="card-header">
          <h1>Join Vaani</h1>
          <p>Realtime voice translator</p>
        </div>

        <div className="card-body">
          <div className="form-group">
            <label htmlFor="meetingId">Vaani Meeting ID</label>
            <input
              id="meetingId"
              type="text"
              className="form-control"
              value={meetingId}
              onChange={(e) => setMeetingId(e.target.value)}
              onKeyPress={handleKeyPress}
              placeholder="Enter your Vaani Meeting ID"
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
            onClick={() => handleValidate()}
            disabled={loading || !meetingId}
          >
            {loading ? 'Validating...' : 'Join Meeting'}
          </button>

          <div className="help-text">
            <p>
              Don't have a link? Contact your organizer to get access.
            </p>
          </div>
        </div>
      </div>
    </div>
  )
}

export default PublicMeetingAccess
