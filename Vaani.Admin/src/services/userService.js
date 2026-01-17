import apiClient from './api'

const userService = {
  // Get all admin users
  getAllUsers: async () => {
    const response = await apiClient.get('/admin/users')
    return response.data
  },

  // Get user by ID
  getUserById: async (userId) => {
    const response = await apiClient.get(`/admin/users/${userId}`)
    return response.data
  },

  // Create new admin user
  createUser: async (userData) => {
    const response = await apiClient.post('/admin/users', userData)
    return response.data
  },

  // Update admin user
  updateUser: async (userData) => {
    const response = await apiClient.put(`/admin/users/${userData.userId}`, userData)
    return response.data
  },

  // Delete admin user
  deleteUser: async (userId) => {
    const response = await apiClient.delete(`/admin/users/${userId}`)
    return response.data
  },

  // Reset user password
  resetPassword: async (userId, newPassword) => {
    const response = await apiClient.post(`/admin/users/${userId}/reset-password`, { newPassword })
    return response.data
  }
}

export default userService
