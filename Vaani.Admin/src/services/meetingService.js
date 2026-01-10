import apiClient from './api'

export const meetingService = {
  getAllMeetings: async () => {
    const response = await apiClient.get('/admin/adminmeetings')
    return response.data
  },

  getMeetingById: async (meetingId) => {
    const response = await apiClient.get(`/admin/adminmeetings/${meetingId}`)
    return response.data
  },

  createMeeting: async (meetingData) => {
    const response = await apiClient.post('/admin/adminmeetings', meetingData)
    return response.data
  },

  updateMeeting: async (meetingId, meetingData) => {
    const response = await apiClient.put(`/admin/adminmeetings/${meetingId}`, meetingData)
    return response.data
  },

  deleteMeeting: async (meetingId) => {
    const response = await apiClient.delete(`/admin/adminmeetings/${meetingId}`)
    return response.data
  },

  getAzureSubscriptions: async () => {
    const response = await apiClient.get('/admin/AzureSubscriptions')
    return response.data
  }
}
