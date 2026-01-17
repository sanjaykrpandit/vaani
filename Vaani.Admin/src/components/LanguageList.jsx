import { useState, useEffect } from 'react'
import Layout from './Layout'
import languageService from '../services/languageService'
import './LanguageList.css'

function LanguageList() {
  const [languages, setLanguages] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [showModal, setShowModal] = useState(false)
  const [editingLanguage, setEditingLanguage] = useState(null)
  const [form, setForm] = useState({
    id: 0,
    languageCode: '',
    languageName: '',
    languageMaleNeural: '',
    languageFemaleNeural: '',
    isActive: true
  })

  useEffect(() => {
    loadLanguages()
  }, [])

  const loadLanguages = async () => {
    try {
      setLoading(true)
      setError('')
      const data = await languageService.getAllLanguages()
      setLanguages(data)
    } catch (err) {
      setError('Failed to load languages')
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  const openAdd = () => {
    setEditingLanguage(null)
    setForm({ id: 0, languageCode: '', languageName: '', languageMaleNeural: '', languageFemaleNeural: '', isActive: true })
    setShowModal(true)
  }

  const openEdit = (lang) => {
    setEditingLanguage(lang)
    setForm({
      id: lang.id,
      languageCode: lang.languageCode,
      languageName: lang.languageName,
      languageMaleNeural: lang.languageMaleNeural,
      languageFemaleNeural: lang.languageFemaleNeural,
      isActive: lang.isActive
    })
    setShowModal(true)
  }

  const handleDelete = async (languageCode) => {
    if (!window.confirm('Are you sure you want to delete this language?')) return
    try {
      await languageService.deleteLanguage(languageCode)
      setLanguages(languages.filter(l => l.languageCode !== languageCode))
    } catch (err) {
      alert('Failed to delete language')
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
      if (editingLanguage) {
        await languageService.updateLanguage(form)
      } else {
        await languageService.createLanguage(form)
      }
      setShowModal(false)
      loadLanguages()
    } catch (err) {
      alert(err.response?.data?.message || 'Failed to save language')
    }
  }

  return (
    <Layout>
      <div >
        <div className="page-header">
          <h1 className="h1-header">Languages</h1>
          <button className="btn btn-sm btn-primary" onClick={openAdd}>+ Add Language</button>
        </div>

        {error && <div className="error-message">{error}</div>}

        {loading ? (
          <div>Loading languages...</div>
        ) : (

           <div className="meeting-list">
      <table className="meeting-table">
          
            <thead>
              <tr>
                <th>Code</th>
                <th>Name</th>
                <th>Male Neural</th>
                <th>Female Neural</th>
                <th>Active</th>
                <th  align='center'></th>
              </tr>
            </thead>
            <tbody>
              {languages.map(lang => (
                <tr key={lang.id}>
                  <td>{lang.languageCode}</td>
                  <td>{lang.languageName}</td>
                  <td>{lang.languageMaleNeural}</td>
                  <td>{lang.languageFemaleNeural}</td>
                  <td>{lang.isActive ? 'Yes' : 'No'}</td>
                  <td align='right'>
                    <button className="btn btn-xsm" onClick={() => openEdit(lang)}>
                       ✏️
                    </button>&nbsp;&nbsp;
                    <button className="btn btn-xsm fnt-red" onClick={() => handleDelete(lang.languageCode)}> 🗑 </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          </div>
        )}

        {showModal && (
          <div className="modal-overlay">
            <div className="modal">
              <h2>{editingLanguage ? 'Edit Language' : 'Add Language'}</h2>
              <form onSubmit={handleSubmit} className="modal-form">
                <div className="form-group">
                  <label>Language Code *</label>
                  <input name="languageCode" value={form.languageCode} onChange={handleChange} required />
                </div>
                <div className="form-group">
                  <label>Language Name *</label>
                  <input name="languageName" value={form.languageName} onChange={handleChange} required />
                </div>
                <div className="form-group">
                  <label>Male Neural *</label>
                  <input name="languageMaleNeural" value={form.languageMaleNeural} onChange={handleChange} required />
                </div>
                <div className="form-group">
                  <label>Female Neural *</label>
                  <input name="languageFemaleNeural" value={form.languageFemaleNeural} onChange={handleChange} required />
                </div>
                <div className="form-group">
                  <label>
                    <input type="checkbox" name="isActive" checked={form.isActive} onChange={handleChange} /> Active
                  </label>
                </div>
                <div className="form-actions">
                  <button type="button" className="btn btn-xsm btn-secondary" onClick={() => setShowModal(false)}>Cancel</button>
                  <button type="submit" className="btn btn-xsm  btn-primary">{editingLanguage ? 'Update' : 'Create'}</button>
                </div>
              </form>
            </div>
          </div>
        )}
      </div>
    </Layout>
  )
}

export default LanguageList
