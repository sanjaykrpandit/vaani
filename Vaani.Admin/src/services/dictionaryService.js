import apiClient from './api'

export const dictionaryService = {
  getAll: async (languageCode) => {
    const params = languageCode ? { languageCode } : {}
    const response = await apiClient.get('/admin/conversationaldictionary', { params })
    return response.data
  },

  getById: async (id) => {
    const response = await apiClient.get(`/admin/conversationaldictionary/${id}`)
    return response.data
  },

  create: async (data) => {
    const response = await apiClient.post('/admin/conversationaldictionary', data)
    return response.data
  },

  update: async (id, data) => {
    const response = await apiClient.put(`/admin/conversationaldictionary/${id}`, data)
    return response.data
  },

  delete: async (id) => {
    const response = await apiClient.delete(`/admin/conversationaldictionary/${id}`)
    return response.data
  }
}

export default dictionaryService
