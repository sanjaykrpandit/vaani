import api from './api'

/**
 * Generate an encrypted secure token for public meeting access
 * Backend encrypts: meetingId + PublicToken (from DB)
 * @param {string} meetingId - The meeting ID to generate token for
 * @returns {Promise<string>} - The generated encrypted token
 */
export const generateMeetingToken = async (meetingId) => {
  try {
    const response = await api.post(`/admin/meetinglink/${meetingId}/generate-token`)
    return response.data.token
  } catch (error) {
    console.error('Error generating meeting token:', error)
    throw error
  }
}

/**
 * Validate a meeting token for public access
 * Backend decrypts token and validates meeting
 * @param {string} meetingId - The meeting ID entered by user
 * @param {string} token - The encrypted token from URL
 * @returns {Promise<object>} - Meeting details with isValid flag and downloadLink
 */
export const validateMeetingToken = async (meetingId, token) => {
  try {
    // This is a public endpoint that doesn't require admin authentication
    const response = await api.post('/meetings/validate-token', {
      meetingId,
      token
    })
    return response.data
  } catch (error) {
    console.error('Error validating meeting token:', error)
    throw error
  }
}
