import { useState, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import Layout from '../components/Layout'
import { meetingService } from '../services/meetingService'
import languageService from '../services/languageService'

function EditMeeting() {
  const { meetingId } = useParams()
  const navigate = useNavigate()
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [languages, setLanguages] = useState([])
  const [langsLoading, setLangsLoading] = useState(true)
  const [formData, setFormData] = useState({
    meetingName: '',
    azureSubscriptionId: '',
    validFrom: '',
    validUntil: '',
    isActive: true,
    meetingLanguage: 'en-US'
  })

  useEffect(() => {
    loadMeeting()
    loadLanguages()
  }, [meetingId])

  const formatDateForInput = (dateStr) => {
    if (!dateStr) return ''
    const d = new Date(dateStr)
    const pad = (n) => String(n).padStart(2, '0')
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
  }

  const loadLanguages = async () => {
    try {
      setLangsLoading(true)
      const data = await languageService.getAllLanguages()
      setLanguages(data || [])
    } catch (err) {
      console.error('Failed to load languages', err)
    } finally {
      setLangsLoading(false)
    }
  }

  const loadMeeting = async () => {
    try {
      setLoading(true)
      const meeting = await meetingService.getMeetingById(meetingId)

      // Convert dates to datetime-local format
      setFormData({
        meetingName: meeting.meetingName,
        azureSubscriptionId: meeting.azureSubscriptionId,
        validFrom: formatDateForInput(meeting.validFrom),
        validUntil: formatDateForInput(meeting.validUntil),
        isActive: meeting.isActive,
        meetingLanguage: meeting.meetingLanguage || 'en-US'
      })
    } catch (err) {
      setError('Failed to load meeting')
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

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
    setSaving(true)

    try {
      // Validate dates
      const validFrom = new Date(formData.validFrom)
      const validUntil = new Date(formData.validUntil)

      if (validUntil <= validFrom) {
        setError('End date must be after start date')
        setSaving(false)
        return
      }

      // Check if meeting has ended - only allow editing upcoming meetings
      const now = new Date()
      if (validUntil < now) {
        setError('Cannot edit completed meetings')
        setSaving(false)
        return
      }

      const updateData = {
        meetingName: formData.meetingName,
        meetingLanguage: formData.meetingLanguage || 'en-US',
        validFrom: validFrom.toISOString(),
        validUntil: validUntil.toISOString(),
        isActive: formData.isActive
      }

      await meetingService.updateMeeting(meetingId, updateData)
      navigate('/dashboard')
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to update meeting')
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return (
      <Layout>
        <div className="loading">Loading meeting...</div>
      </Layout>
    )
  }

  return (
    <Layout>
      <div className="page-container">
        <div className="page-header">
          <h1 className="h1-header"> Edit Meeting</h1>
        </div>

        <form onSubmit={handleSubmit} className="meeting-form">
          {error && (
            <div className="error-message">
              {error}
            </div>
          )}

          <div className="form-group">
            <label htmlFor="meetingId">Meeting ID</label>
            <input
              id="meetingId"
              type="text"
              value={meetingId}
              disabled
              className="disabled"
            />
            <small>Meeting ID cannot be changed</small>
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
                name="isActive"
                checked={formData.isActive}
                onChange={handleChange}
              />
              <span>Active</span>
            </label>
          </div>
          <div className="form-actions">
            <button
              type="button"
              className="btn btn-sm btn-secondary"
              onClick={() => navigate('/dashboard')}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-sm btn-primary"
              disabled={saving}
            >
              {saving ? 'Saving...' : 'Save Changes'}
            </button>
          </div>
        </form>
      </div>
    </Layout>
  )
}

export default EditMeeting
