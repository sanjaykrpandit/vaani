import apiClient from './api'

export const authService = {
  login: async (userId, password) => {
    const response = await apiClient.post('/admin/login', {
      userId,
      password
    })
    return response.data
  }
}
