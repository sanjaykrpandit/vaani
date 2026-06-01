import { createContext, useContext, useState, useEffect } from 'react'
import { authService } from '../services/authService'

const AuthContext = createContext(null)

export const AuthProvider = ({ children }) => {
  const [user, setUser] = useState(null)
  const [token, setToken] = useState(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    // Check if user is already logged in
    const storedToken = localStorage.getItem('adminToken')
    const storedUser = localStorage.getItem('adminUser')
    
    if (storedToken && storedUser) {
      const parsedUser = JSON.parse(storedUser)
      setToken(storedToken)
      setUser({
        ...parsedUser,
        role: parsedUser?.role || 'admin'
      })
    }
    
    setLoading(false)
  }, [])

  const login = async (userId, password) => {
    try {
      const response = await authService.login(userId, password)
      
      if (response.success) {
        setToken(response.accessToken)
        const normalizedUser = {
          ...response.user,
          role: response.user?.role || 'admin'
        }

        setUser(normalizedUser)
        localStorage.setItem('adminToken', response.accessToken)
        localStorage.setItem('adminUser', JSON.stringify(normalizedUser))
        return { success: true }
      }
      
      return { success: false, message: response.message }
    } catch (error) {
      return { 
        success: false, 
        message: error.response?.data?.message || 'Login failed. Please try again.' 
      }
    }
  }

  const logout = () => {
    setToken(null)
    setUser(null)
    localStorage.removeItem('adminToken')
    localStorage.removeItem('adminUser')
  }

  const value = {
    user,
    token,
    login,
    logout,
    isAuthenticated: !!token,
    loading
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export const useAuth = () => {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
