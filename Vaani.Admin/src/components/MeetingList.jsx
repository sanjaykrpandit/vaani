import { useState } from 'react'
import { generateMeetingToken } from '../services/tokenService'

function MeetingList({
  analysis,
  logo,
  meetings,
  onEdit,
  onDelete,
  onLaunch,
  onLaunchVaaniTranslation,
  onViewMetrics,
  canLaunch = true,
  canViewMetrics = true,
  canManageMeetings = true,
  canGenerateLinks = true
}) {
  const [copyingKey, setCopyingKey] = useState(null)

  const copyToClipboard = async (value) => {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(value)
      return
    }

    const textarea = document.createElement('textarea')
    textarea.value = value
    textarea.style.position = 'fixed'
    textarea.style.opacity = '0'
    document.body.appendChild(textarea)
    textarea.focus()
    textarea.select()

    try {
      document.execCommand('copy')
    } finally {
      document.body.removeChild(textarea)
    }
  }

  const handleCopyLink = async (meetingId, appType = 'audio') => {
    const currentCopyKey = `${meetingId}-${appType}`

    try {
      setCopyingKey(currentCopyKey)
      const token = await generateMeetingToken(meetingId)
      const origin = window.location.origin
      const publicUrl = `${origin}/public-access?token=${encodeURIComponent(token)}&appType=${appType}`

      await copyToClipboard(publicUrl)
      setTimeout(() => setCopyingKey(null), 2000)
    } catch (error) {
      console.error('Failed to copy link:', error)
      alert('Failed to generate meeting link. Please try again.')
      setCopyingKey(null)
    }
  }


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

  const getMeetingStatus = (validFrom, validUntil) => {
    const now = new Date()
    const start = new Date(validFrom)
    const end = new Date(validUntil)

    if (now < start) return 'upcoming'
    if (now >= start && now <= end) return 'running'
    return 'completed'
  }

  const canEdit = (validUntil) => {
    return new Date(validUntil) > new Date()
  }

  // 🔹 Filter meetings by status 

  if (!meetings.length) {
    return (
      <div className="empty-state">
        <p>No meetings found</p>
      </div>
    )
  }

  return (
    <div className="meeting-list">
      <table className="meeting-table">
        <thead>
          <tr>
            <th>Meeting ID</th>
            <th>Meeting Name</th>
            <th>Meeting Language</th>
            <th>Start Date</th>
            <th>End Date</th>
            <th></th>
            <th>Status</th>
            <th>Actions</th>
          </tr>
        </thead>

        <tbody>
          {meetings.map((meeting) => {
            const status = getMeetingStatus(
              meeting.validFrom,
              meeting.validUntil
            )

            const isEditable = canEdit(meeting.validUntil)

            return (
              <tr key={meeting.meetingId} className={`meeting-row ${status}`}>
                <td>{meeting.meetingId}</td>

                <td>
                  <strong>{meeting.meetingName}</strong>
                  {!meeting.isActive && (
                    <span className="badge badge-inactive ms-2">
                      Inactive
                    </span>
                  )}
                </td>

                <td>{meeting.meetingLanguage}</td>
                <td>{formatDate(meeting.validFrom)}</td>
                <td>{formatDate(meeting.validUntil)}</td>

                {/* Launch */}
                <td className="launch-cell">
                  {status === 'running' && meeting.isActive && (
                    <div className="launch-button-group launch-button-group-combined">
                      <div className="launch-pair">
                        <button
                          className="btn btn-xsm btn-primary"
                          onClick={() => onLaunch(meeting.meetingId)}
                          title="Audio"
                        >
                          Audio
                          <img src={logo} alt="logo" height="10" />
                        </button>
                        <button
                          className="btn btn-xsm"
                          onClick={() => handleCopyLink(meeting.meetingId, 'audio')}
                          title="Copy Audio Public Access Link"
                          disabled={copyingKey === `${meeting.meetingId}-audio`}
                        >
                          {copyingKey === `${meeting.meetingId}-audio` ? '✓' : '🔗'}
                        </button>
                      </div>
                      <div className="launch-pair">
                        <button
                          className="btn btn-xsm btn-secondary"
                          onClick={() => onLaunchVaaniTranslation(meeting.meetingId)}
                          title="Subtitle"
                        >
                          Subtitle
                          <img src={logo} alt="logo" height="10" />
                        </button>
                        <button
                          className="btn btn-xsm"
                          onClick={() => handleCopyLink(meeting.meetingId, 'subtitle')}
                          title="Copy Subtitle Public Access Link"
                          disabled={copyingKey === `${meeting.meetingId}-subtitle`}
                        >
                          {copyingKey === `${meeting.meetingId}-subtitle` ? '✓' : '🔗'}
                        </button>
                      </div>
                    </div>
                  )}

                  {status === 'completed' && (
                    <span className="badge badge-archived">
                      Closed
                    </span>
                  )}

                </td>
                {/* Status */}
                <td>
                  <span className="status-with-dot badge badge-completed">
                    <span className={`status-dot ${status}`} />
                    {status}
                  </span>
                </td>

                {/* Actions */}
                <td className="actions">
                  {canViewMetrics && (
                    <button
                      className="btn btn-xsm"
                      onClick={() => onViewMetrics(meeting.meetingId)}
                      title="View Analytics"
                    >
                      <img src={analysis} alt="Analytics" height="16" />
                    </button>
                  )}

                  {canManageMeetings && isEditable && (
                    <button
                      className="btn btn-xsm"
                      onClick={() => onEdit(meeting.meetingId)}
                    >
                      ✏️
                    </button>
                  )}

                  {canManageMeetings && isEditable && (
                    <button
                      className="btn btn-xsm"
                      onClick={() => onDelete(meeting.meetingId)}
                    >
                      🗑
                    </button>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

export default MeetingList
