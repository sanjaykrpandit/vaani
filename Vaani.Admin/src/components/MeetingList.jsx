function MeetingList({ meetings, onEdit, onDelete }) {
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

  const getMeetingStatus = (validUntil) => {
    return new Date(validUntil) > new Date() ? 'upcoming' : 'completed'
  }

  const canEdit = (validUntil) => {
    return new Date(validUntil) > new Date()
  }

  if (meetings.length === 0) {
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
            <th>Subscription ID</th>
            <th>Start Date</th>
            <th>End Date</th>
            <th>Status</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {meetings.map((meeting) => {
            const status = getMeetingStatus(meeting.validUntil)
            const isEditable = canEdit(meeting.validUntil)
            
            return (
              <tr key={meeting.meetingId} className={`meeting-row ${status}`}>
                <td>
                  <span className="meeting-id">{meeting.meetingId}</span>
                </td>
                <td>
                  <strong>{meeting.meetingName}</strong>
                  {!meeting.isActive && (
                    <span className="badge badge-inactive">Inactive</span>
                  )}
                </td>
                <td>{meeting.azureSubscriptionId}</td>
                <td>{formatDate(meeting.validFrom)}</td>
                <td>{formatDate(meeting.validUntil)}</td>
                <td>
                  <span className={`badge badge-${status}`}>
                    {status}
                  </span>
                </td>
                <td className="actions">
                  {isEditable && (
                    <button
                      className="btn btn-sm btn-secondary"
                      onClick={() => onEdit(meeting.meetingId)}
                      title="Edit meeting"
                    >
                      Edit
                    </button>
                  )}
                  {isEditable && (
                    <button
                      className="btn btn-sm btn-danger"
                      onClick={() => onDelete(meeting.meetingId)}
                      title="Delete meeting"
                    >
                      Delete
                    </button>
                  )}
                  {!isEditable && (
                    <span className="text-muted">No actions</span>
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
