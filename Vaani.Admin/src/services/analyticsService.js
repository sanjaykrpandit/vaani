import apiClient from './api'

export const analyticsService = {
  getSessionMetrics: async (meetingId) => {
    const response = await apiClient.get(`/admin/adminmeetings/${meetingId}/metrics`)
    return response.data
  },

  getSessionLogs: async (sessionId) => {
    const response = await apiClient.get(`/admin/adminmeetings/sessions/${sessionId}/logs`)
    return response.data
  }
}
