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
  onViewMetrics
}) {
  const [copyingId, setCopyingId] = useState(null)

  const handleCopyLink = async (meetingId) => {
    try {
      setCopyingId(meetingId)

      // Generate token from backend
      const token = await generateMeetingToken(meetingId)

      // Create public access URL with only token parameter
      const origin = window.location.origin
      const publicUrl = `${origin}/public-access?token=${token}`

      // Copy to clipboard
      await navigator.clipboard.writeText(publicUrl)

      // Show success feedback
      setTimeout(() => setCopyingId(null), 2000)
    } catch (error) {
      console.error('Failed to copy link:', error)
      alert('Failed to generate meeting link. Please try again.')
      setCopyingId(null)
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
                <td>
                  {status === 'running' && meeting.isActive && (
                    <div className="d-inline-flex align-items-center gap-2">
                      <button
                        className="btn btn-xsm btn-primary d-inline-flex align-items-center gap-2"
                        onClick={() => onLaunch(meeting.meetingId)}
                      >
                        Launch
                        <img src={logo} alt="logo" height="10" />
                      </button>
                      <button
                        className="btn btn-xsm btn-secondary"
                        onClick={() => onLaunchVaaniTranslation(meeting.meetingId)}
                      >
                        Launch Vaani Translation
                      </button>
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
                  <button
                    className="btn btn-xsm"
                    onClick={() => onViewMetrics(meeting.meetingId)}
                    title="View Analytics"
                  >
                    <img src={analysis} alt="Analytics" height="16" />
                  </button>

                  {status === 'running' && meeting.isActive && (
                    <button
                      className="btn btn-xsm"
                      onClick={() => handleCopyLink(meeting.meetingId)}
                      title="Copy Public Access Link"
                      disabled={copyingId === meeting.meetingId}
                    >
                      {copyingId === meeting.meetingId ? '✓' : '🔗'}
                    </button>
                  )}

                  {isEditable && (
                    <button
                      className="btn btn-xsm"
                      onClick={() => onEdit(meeting.meetingId)}
                    >
                      ✏️
                    </button>
                  )}

                  {isEditable && (
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
