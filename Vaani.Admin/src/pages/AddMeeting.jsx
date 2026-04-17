import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import Layout from '../components/Layout'
import { meetingService } from '../services/meetingService'
import languageService from '../services/languageService'

function AddMeeting() {
  const navigate = useNavigate()
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [languages, setLanguages] = useState([])
  const [langsLoading, setLangsLoading] = useState(true)

  const [formData, setFormData] = useState({
    meetingId: '',
    meetingName: '',
    meetingLanguage: 'en-US',
    validFrom: '',
    validUntil: '',
    requiresPassword: false,
    password: ''
  })

  useEffect(() => {
    let mounted = true
    setLangsLoading(true)
    languageService.getAllLanguages()
      .then(data => {
        if (!mounted) return
        setLanguages(data || [])
        if (!formData.meetingLanguage) {
          setFormData(prev => ({ ...prev, meetingLanguage: 'en-US' }))
        }
      })
      .catch(() => {
        if (!mounted) return
        setError('Failed to load languages')
      })
      .finally(() => {
        if (!mounted) return
        setLangsLoading(false)
      })

    return () => { mounted = false }
  }, [])

  const handleChange = (e) => {
    const { name, value, type, checked } = e.target
    setFormData(prev => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : value
    }))
  }

  const handleSubmit = async (e) => {
    e.preventDefault()
    setError('')
    setLoading(true)

    try {
      // Validate dates
      const validFrom = new Date(formData.validFrom)
      const validUntil = new Date(formData.validUntil)

      if (validUntil <= validFrom) {
        setError('End date must be after start date')
        setLoading(false)
        return
      }

      // If password is required ensure password is provided
      if (formData.requiresPassword && !formData.password) {
        setError('Password is required when "Require password" is enabled')
        setLoading(false)
        return
      }

      const meetingData = {
        meetingId: formData.meetingId,
        meetingName: formData.meetingName,
        meetingLanguage: formData.meetingLanguage || 'en-US',
        validFrom: validFrom.toISOString(),
        validUntil: validUntil.toISOString(),
        requiresPassword: formData.requiresPassword,
        ...(formData.password ? { password: formData.password } : {})
      }

      // Only include password if protection is enabled
      if (formData.requiresPassword && formData.password) {
        meetingData.password = formData.password
      }

      await meetingService.createMeeting(meetingData)
      navigate('/dashboard')
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to create meeting')
    } finally {
      setLoading(false)
    }
  }

  return (
    <Layout>
      <div className="page-container">
        <div className="page-header">
          <h1 className="h1-header">Add New Meeting</h1>
        </div>

        <form onSubmit={handleSubmit} className="meeting-form">
          {error && (
            <div className="error-message">
              {error}
            </div>
          )}

          <div className="form-group">
            <label htmlFor="meetingId">Meeting ID *</label>
            <input
              id="meetingId"
              name="meetingId"
              type="text"
              value={formData.meetingId}
              onChange={handleChange}
              placeholder="e.g., MEET-2024-001"
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="meetingName">Meeting Name *</label>
            <input
              id="meetingName"
              name="meetingName"
              type="text"
              value={formData.meetingName}
              onChange={handleChange}
              placeholder="e.g., Q1 2024 Planning Meeting"
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="meetingLanguage">Meeting Language *</label>
            {langsLoading ? (
              <div>Loading languages...</div>
            ) : (
              <select
                id="meetingLanguage"
                name="meetingLanguage"
                value={formData.meetingLanguage}
                onChange={handleChange}
                required
              >
                <option value="">-- Select language --</option>
                {languages.map(l => (
                  <option key={l.languageCode} value={l.languageCode}>{l.languageName}</option>
                ))}
              </select>
            )}
          </div>

          <div className="form-group">
            <label className="checkbox-label">
              <input
                type="checkbox"
                name="requiresPassword"
                checked={formData.requiresPassword}
                onChange={handleChange}
              />
              <span>Require password to join meeting</span>
            </label>
          </div>

          {formData.requiresPassword && (
            <>
              <div className="form-group">
                <label htmlFor="password">Meeting Password *</label>
                <input
                  id="password"
                  name="password"
                  type="password"
                  value={formData.password}
                  onChange={handleChange}
                  placeholder="Enter meeting password"
                  required={formData.requiresPassword}
                  minLength={6}
                />
                <small>Minimum 6 characters</small>
              </div>

              <div className="form-group">
                <label htmlFor="confirmPassword">Confirm Password *</label>
                <input
                  id="confirmPassword"
                  name="confirmPassword"
                  type="password"
                  value={formData.confirmPassword}
                  onChange={handleChange}
                  placeholder="Confirm meeting password"
                  required={formData.requiresPassword}
                />
              </div>
            </>
          )}

          <div className="form-row">
            <div className="form-group">
              <label htmlFor="validFrom">Start Date *</label>
              <input
                id="validFrom"
                name="validFrom"
                type="datetime-local"
                value={formData.validFrom}
                onChange={handleChange}
                required
              />
            </div>

            <div className="form-group">
              <label htmlFor="validUntil">End Date *</label>
              <input
                id="validUntil"
                name="validUntil"
                type="datetime-local"
                value={formData.validUntil}
                onChange={handleChange}
                required
              />
            </div>
          </div>

          <div className="form-group">
            <label className="checkbox-label">
              <input
                type="checkbox"
                name="requiresPassword"
                checked={formData.requiresPassword}
                onChange={handleChange}
              />
              <span>Require password</span>
            </label>
          </div>

          {formData.requiresPassword && (
            <div className="form-group">
              <label htmlFor="password">Meeting Password *</label>
              <input
                id="password"
                name="password"
                type="password"
                value={formData.password}
                onChange={handleChange}
                placeholder="Enter meeting password"
                required={formData.requiresPassword}
              />
              <small>Participants will need this password to join the meeting.</small>
            </div>
          )}

          <div className="form-actions">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => navigate('/dashboard')}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={loading}
            >
              {loading ? 'Creating...' : 'Create Meeting'}
            </button>
          </div>
        </form>
      </div>
    </Layout>
  )
}

export default AddMeeting