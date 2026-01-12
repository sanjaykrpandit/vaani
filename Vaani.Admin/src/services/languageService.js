import apiClient from './api'

export const languageService = {
  getAllLanguages: async () => {
    const response = await apiClient.get('/language')
    return response.data
  },

  createLanguage: async (languageData) => {
    const response = await apiClient.post('/language', languageData)
    return response.data
  },

  updateLanguage: async (languageData) => {
    const response = await apiClient.put('/language', languageData)
    return response.data
  },

  deleteLanguage: async (languageCode) => {
    const response = await apiClient.delete(`/language/${encodeURIComponent(languageCode)}`)
    return response.data
  }
}

export default languageService
