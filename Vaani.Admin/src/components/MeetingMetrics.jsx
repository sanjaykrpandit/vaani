import { useState } from 'react'
import {
  LineChart,
  Line,
  BarChart,
  Bar,
  PieChart,
  Pie,
  Cell,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer
} from 'recharts'
import './MeetingMetrics.css'
import group from '../Assets/group.png'
import sessions from '../Assets/inprocess.png'
import duration from '../Assets/clock.png'
import heart from '../Assets/heart.png'


const COLORS = ['#0088FE', '#00C49F', '#FFBB28', '#FF8042', '#8884d8', '#82ca9d']

function MeetingMetrics({ metrics }) {
  const [selectedSession, setSelectedSession] = useState(null)

  if (!metrics) {
    return <div className="no-data">No metrics available</div>
  }

  // Prepare status data for pie chart
  const statusData = [
    { name: 'Active', value: metrics.activeSessions },
    { name: 'Ended', value: metrics.endedSessions },
    { name: 'Expired', value: metrics.expiredSessions }
  ].filter(item => item.value > 0)

  // Format timeline data
  const timelineData = metrics.sessionTimeline.map(item => ({
    date: new Date(item.date).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }),
    sessions: item.sessionCount,
    heartbeats: item.totalHeartbeats
  }))

  // Device breakdown data
  const deviceData = metrics.deviceBreakdown.map(device => ({
    name: device.deviceName || 'Unknown',
    sessions: device.sessionCount,
    minutes: Math.round(device.totalMinutes)
  }))

  const formatDuration = (minutes) => {
    if (minutes < 60) return `${Math.round(minutes)}m`
    const hours = Math.floor(minutes / 60)
    const mins = Math.round(minutes % 60)
    return `${hours}h ${mins}m`
  }

  return (
    <div className="meeting-metrics">
      {/* Summary Cards */}
      <div className="metrics-summary">



        <div className="metric-card">
          <div className="metric-icon"> <img src={group} width={48} className="icon" /></div>
          <div className="metric-content">
            <h3>Total Users</h3>
            <p className="metric-value">{metrics.deviceBreakdown.length}</p>
          </div>
        </div>


        <div className="metric-card">
          <div className="metric-icon"> <img src={group} width={48} className="icon" /></div>
          <div className="metric-content">
            <h3>Total Sessions</h3>
            <p className="metric-value">{metrics.totalSessions}</p>
          </div>
        </div>
        
        <div className="metric-card">
          <div className="metric-icon"><img src={sessions} width={48} className="icon" /></div>
          <div className="metric-content">
            <h3>Active Sessions</h3>
            <p className="metric-value">{metrics.activeSessions}</p>
          </div>
        </div>
        
        <div className="metric-card">
          <div className="metric-icon"><img src={duration} width={48} className="icon" /></div>
          <div className="metric-content">
            <h3>Avg Duration</h3>
            <p className="metric-value">
              {formatDuration(metrics.averageSessionDurationMinutes)}
            </p>
          </div>
        </div>
        
        {/* <div className="metric-card">
          <div className="metric-icon"><img src={heart} width={48} className="icon" /></div>
          <div className="metric-content">
            <h3>Total Heartbeats</h3>
            <p className="metric-value">{metrics.totalHeartbeats}</p>
          </div>
        </div> */}
      </div>

      {/* Charts Section */}
      <div className="metrics-charts">
        {/* Session Timeline */}
        {timelineData.length > 0 && (
          <div className="chart-container">
            <h3>Session Activity Over Time</h3>
            <ResponsiveContainer width="100%" height={300}>
              <LineChart data={timelineData}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" />
                <YAxis yAxisId="left" />
                <YAxis yAxisId="right" orientation="right" />
                <Tooltip />
                <Legend />
                <Line 
                  yAxisId="left"
                  type="monotone" 
                  dataKey="sessions" 
                  stroke="#8884d8" 
                  strokeWidth={2}
                  name="Sessions"
                />
                <Line 
                  yAxisId="right"
                  type="monotone" 
                  dataKey="heartbeats" 
                  stroke="#82ca9d" 
                  strokeWidth={2}
                  name="Heartbeats"
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}

        {/* Status Distribution */}
        {statusData.length > 0 && (
          <div className="chart-container half">
            <h3>Session Status Distribution</h3>
            <ResponsiveContainer width="100%" height={300}>
              <PieChart>
                <Pie
                  data={statusData}
                  cx="50%"
                  cy="50%"
                  labelLine={false}
                  label={({ name, percent }) => `${name}: ${(percent * 100).toFixed(0)}%`}
                  outerRadius={80}
                  fill="#8884d8"
                  dataKey="value"
                >
                  {statusData.map((entry, index) => (
                    <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                  ))}
                </Pie>
                <Tooltip />
              </PieChart>
            </ResponsiveContainer>
          </div>
        )}

        {/* Device Breakdown */}
        {deviceData.length > 0 && (
          <div className="chart-container half">
            <h3>Sessions by Device</h3>
            <ResponsiveContainer width="100%" height={300}>
              <BarChart data={deviceData}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="name" />
                <YAxis />
                <Tooltip />
                <Legend />
                <Bar dataKey="sessions" fill="#8884d8" name="Sessions" />
                <Bar dataKey="minutes" fill="#82ca9d" name="Total Minutes" />
              </BarChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>

      {/* Device Details Table */}
      {metrics.deviceBreakdown.length > 0 && (
        <div className="device-details">
          <h3>Device Breakdown</h3>
          <table className="metrics-table">
            <thead>
              <tr>
                <th>Device Name</th>
                <th>Device ID</th>
                <th>Sessions</th>
                <th>Total Duration</th>
                <th>Last Activity</th>
              </tr>
            </thead>
            <tbody>
              {metrics.deviceBreakdown.map((device, index) => (
                <tr key={index}>
                  <td>{device.deviceName || 'Unknown'}</td>
                  <td className="device-id">{device.deviceId}</td>
                  <td>{device.sessionCount}</td>
                  <td>{formatDuration(device.totalMinutes)}</td>
                  <td>{new Date(device.lastActivity).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Recent Sessions */}
      {metrics.recentSessions.length > 0 && (
        <div className="recent-sessions">
          <h3>Recent Sessions</h3>
          <table className="metrics-table">
            <thead>
              <tr>
                <th>Device</th>
                <th>App Version</th>
                <th>Started</th>
                <th>Duration</th>
                <th>Status</th>
                <th>Heartbeats</th>
              </tr>
            </thead>
            <tbody>
              {metrics.recentSessions.map((session) => (
                <tr 
                  key={session.sessionId}
                  onClick={() => setSelectedSession(session)}
                  className="clickable"
                >
                  <td>{session.deviceName || 'Unknown'}</td>
                  <td>{session.appVersion}</td>
                  <td>{new Date(session.startedAt).toLocaleString()}</td>
                  <td>{formatDuration(session.durationMinutes)}</td>
                  <td>
                    <span className={`status-badge ${session.status.toLowerCase()}`}>
                      {session.status}
                    </span>
                  </td>
                  <td>{session.heartbeatCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Session Details Modal */}
      {selectedSession && (
        <div className="modal-overlay" onClick={() => setSelectedSession(null)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Session Details</h3>
              <button className="close-btn" onClick={() => setSelectedSession(null)}>x</button>
            </div>
            <div className="modal-body">
              <div className="detail-row">
                <strong>Session ID:</strong>
                <span>{selectedSession.sessionId}</span>
              </div>
              <div className="detail-row">
                <strong>Device Name:</strong>
                <span>{selectedSession.deviceName}</span>
              </div>
              <div className="detail-row">
                <strong>Device ID:</strong>
                <span>{selectedSession.deviceId}</span>
              </div>
              <div className="detail-row">
                <strong>App Version:</strong>
                <span>{selectedSession.appVersion}</span>
              </div>
              <div className="detail-row">
                <strong>Started:</strong>
                <span>{new Date(selectedSession.startedAt).toLocaleString()}</span>
              </div>
              {selectedSession.endedAt && (
                <div className="detail-row">
                  <strong>Ended:</strong>
                  <span>{new Date(selectedSession.endedAt).toLocaleString()}</span>
                </div>
              )}
              <div className="detail-row">
                <strong>Last Heartbeat:</strong>
                <span>{new Date(selectedSession.lastHeartbeat).toLocaleString()}</span>
              </div>
              <div className="detail-row">
                <strong>Duration:</strong>
                <span>{formatDuration(selectedSession.durationMinutes)}</span>
              </div>
              <div className="detail-row">
                <strong>Status:</strong>
                <span className={`status-badge ${selectedSession.status.toLowerCase()}`}>
                  {selectedSession.status}
                </span>
              </div>
              <div className="detail-row">
                <strong>Total Heartbeats:</strong>
                <span>{selectedSession.heartbeatCount}</span>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export default MeetingMetrics
