import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import Layout from '../components/Layout'
import { meetingService } from '../services/meetingService'

function AddMeeting() {
  const navigate = useNavigate()
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [formData, setFormData] = useState({
    meetingId: '',
    meetingName: '',
    azureSubscriptionId: '',
    validFrom: '',
    validUntil: ''
  })

  const handleChange = (e) => {
    const { name, value } = e.target
    setFormData(prev => ({
      ...prev,
      [name]: value
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

      const meetingData = {
        meetingId: formData.meetingId,
        meetingName: formData.meetingName,
        azureSubscriptionId: parseInt(formData.azureSubscriptionId),
        validFrom: validFrom.toISOString(),
        validUntil: validUntil.toISOString()
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
          <h1>Add New Meeting</h1>
          <button 
            className="btn btn-secondary"
            onClick={() => navigate('/dashboard')}
          >
            Cancel
          </button>
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
            <label htmlFor="azureSubscriptionId">Azure Subscription ID *</label>
            <input
              id="azureSubscriptionId"
              name="azureSubscriptionId"
              type="number"
              value={formData.azureSubscriptionId}
              onChange={handleChange}
              placeholder="e.g., 1"
              required
            />
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
