import { useState, useEffect } from 'react'
import Layout from './Layout'
import userService from '../services/userService'
import './UserList.css'

function UserList() {
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [showModal, setShowModal] = useState(false)
  const [editingUser, setEditingUser] = useState(null)
  const [form, setForm] = useState({
    userId: '',
    FullName: '',
    Email: '',
    Password: ''
  })

  useEffect(() => {
    loadUsers()
  }, [])

  const loadUsers = async () => {
    try {
      setLoading(true)
      setError('')
      const data = await userService.getAllUsers()
      setUsers(data)
    } catch (err) {
      setError('Failed to load users')
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  const openAdd = () => {
    setEditingUser(null)
    setForm({
      userId: '',
      FullName: '',
      Email: '',
      Password: '',
      isActive: true
    })
    setShowModal(true)
  }

  const openEdit = (user) => {
    setEditingUser(user)
    setForm({
      userId: user.userId,
      fullName: user.fullName,
      email: user.email,
      password: user.password,
      isActive: user.isActive
    })
    setShowModal(true)
  }

  const handleDelete = async (userId) => {
    if (!window.confirm('Are you sure you want to delete this user?')) return
    try {
      await userService.deleteUser(userId)
      setUsers(users.filter(u => u.userId !== userId))
    } catch (err) {
      alert('Failed to delete user')
      console.error(err)
    }
  }

  const handleChange = (e) => {
    const { name, value, type, checked } = e.target
    setForm(prev => ({ ...prev, [name]: type === 'checkbox' ? checked : value }))
  }

  const handleSubmit = async (e) => {
    e.preventDefault()
    try {
      if (editingUser) {
        // Don't send password if it's empty during edit
        const updateData = { ...form }
        if (!updateData.password) {
          delete updateData.password
        }
        await userService.updateUser(updateData)
      } else {
        await userService.createUser(form)
      }
      setShowModal(false)
      loadUsers()
    } catch (err) {
      alert(err.response?.data?.message || 'Failed to save user')
    }
  }

  return (
    <Layout>
      <div>
        <div className="page-header">
          <h1 className="h1-header">Admin Users</h1>
          <button className="btn btn-sm btn-primary" onClick={openAdd}>+ Add User</button>
        </div>

        {error && <div className="error-message">{error}</div>}

        {loading ? (
          <div>Loading users...</div>
        ) : (
          <div className="meeting-list">
            <table className="meeting-table">
              <thead>
                <tr>
                  <th>User ID</th>
                  <th>Name</th>
                  <th>Email</th>
                  <th>Role</th>
                  <th>Status</th>
                  <th>Created Date</th>
                  <th align='center'></th>
                </tr>
              </thead>
              <tbody>
                {users.length === 0 ? (
                  <tr>
                    <td colSpan="7" style={{ textAlign: 'center', padding: '2rem' }}>
                      No users found
                    </td>
                  </tr>
                ) : (
                  users.map(user => (
                    <tr key={user.userId}>
                      <td>{user.userId}</td>
                      <td>{user.fullName}</td>
                      <td>{user.email}</td>
                      <td>{user.role || 'Admin'}</td>
                      <td>
                        <span className={`status-badge ${user.isActive ? 'status-active' : 'status-inactive'}`}>
                          {user.isActive ? 'Active' : 'Inactive'}
                        </span>
                      </td>
                      <td>{user.createdDate ? new Date(user.createdDate).toLocaleDateString() : '-'}</td>
                      <td align='right'>
                        <button className="btn btn-xsm" onClick={() => openEdit(user)}>
                          ✏️
                        </button>&nbsp;&nbsp;
                        {/* <button className="btn btn-sm fnt-red" onClick={() => handleDelete(user.userId)}>
                          🗑
                        </button> */}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}

        {showModal && (
          <div className="modal-overlay">
            <div className="modal">
              <h2>{editingUser ? 'Edit User' : 'Add User'}</h2>
              <form onSubmit={handleSubmit} className="modal-form">
                <div className="form-group">
                  <label>User ID *</label>
                  <input
                    name="userId"
                    value={form.userId}
                    onChange={handleChange}
                    required
                    disabled={editingUser !== null}
                  />
                </div>
                <div className="form-group">
                  <label>Name *</label>
                  <input
                    name="fullName"
                    value={form.fullName}
                    onChange={handleChange}
                    required
                  />
                </div>
                <div className="form-group">
                  <label>Email *</label>
                  <input
                    type="email"
                    name="email"
                    value={form.email}
                    onChange={handleChange}
                    required
                  />
                </div>
                <div className="form-group">
                  <label>Password {editingUser ? '(leave blank to keep current)' : '*'}</label>
                  <input
                    type="password"
                    name="password"
                    value={form.password}
                    onChange={handleChange}
                    required={!editingUser}
                  />
                </div>
                {/* <div className="form-group">
                  <label>Role *</label>
                  <select name="role" value={form.role} onChange={handleChange} required>
                    <option value="Admin">Admin</option>
                    <option value="SuperAdmin">Super Admin</option>
                    <option value="Moderator">Moderator</option>
                  </select>
                </div> */}
                <div className="form-group">
                  <label>
                    <input
                      type="checkbox"
                      name="isActive"
                      checked={form.isActive}
                      onChange={handleChange}
                    /> Active
                  </label>
                </div>
                <div className="form-actions">
                  <button type="button" className="btn btn-sm  btn-secondary" onClick={() => setShowModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-sm btn-primary">
                    {editingUser ? 'Update' : 'Create'}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}
      </div>
    </Layout>
  )
}

export default UserList
