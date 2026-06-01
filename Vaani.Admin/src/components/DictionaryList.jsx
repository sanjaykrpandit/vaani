import { useState, useEffect } from 'react'
import Layout from './Layout'
import dictionaryService from '../services/dictionaryService'
import languageService from '../services/languageService'
import './DictionaryList.css'

const MATCH_MODES = ['Contains', 'Exact', 'StartsWith']

const emptyForm = {
  languageCode: '',
  domain: 'general',
  formalText: '',
  conversationalText: '',
  matchMode: 'Contains',
  isActive: true
}

function DictionaryList() {
  const [entries, setEntries] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [filterLanguage, setFilterLanguage] = useState('')
  const [filterDomain, setFilterDomain] = useState('')

  const [languages, setLanguages] = useState([])

  const [showModal, setShowModal] = useState(false)
  const [editingEntry, setEditingEntry] = useState(null)
  const [form, setForm] = useState(emptyForm)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState('')

  useEffect(() => {
    loadEntries()
  }, [filterLanguage, filterDomain])

  useEffect(() => {
    languageService.getAllLanguages()
      .then(data => setLanguages(data.filter(l => l.isActive)))
      .catch(err => console.error('Failed to load languages', err))
  }, [])

  const loadEntries = async () => {
    try {
      setLoading(true)
      setError('')
      const result = await dictionaryService.getAll(filterLanguage || undefined, filterDomain || undefined)
      setEntries(result.items || [])
    } catch (err) {
      setError('Failed to load dictionary entries')
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  const openAdd = () => {
    setEditingEntry(null)
    setForm(emptyForm)
    setFormError('')
    setShowModal(true)
  }

  const openEdit = (entry) => {
    setEditingEntry(entry)
    setForm({
      languageCode: entry.languageCode,
        domain: entry.domain || 'general',
      formalText: entry.formalText,
      conversationalText: entry.conversationalText,
      matchMode: entry.matchMode,
      isActive: entry.isActive
    })
    setFormError('')
    setShowModal(true)
  }

  const handleDelete = async (id) => {
    if (!window.confirm('Are you sure you want to delete this entry?')) return
    try {
      const result = await dictionaryService.delete(id)
      if (!result.success) {
        alert(result.message || 'Failed to delete entry')
        return
      }
      setEntries(entries.filter(e => e.id !== id))
    } catch (err) {
      alert('Failed to delete entry')
      console.error(err)
    }
  }

  const handleChange = (e) => {
    const { name, value, type, checked } = e.target
    setForm(prev => ({ ...prev, [name]: type === 'checkbox' ? checked : value }))
  }

  const handleSubmit = async (e) => {
    e.preventDefault()
    setFormError('')
    setSaving(true)
    try {
      let result
      if (editingEntry) {
        result = await dictionaryService.update(editingEntry.id, {
          domain: form.domain,
          formalText: form.formalText,
          conversationalText: form.conversationalText,
          matchMode: form.matchMode,
          isActive: form.isActive
        })
      } else {
        result = await dictionaryService.create(form)
      }

      if (!result.success) {
        setFormError(result.message || 'Failed to save entry')
        return
      }

      setShowModal(false)
      loadEntries()
    } catch (err) {
      setFormError(err.response?.data?.message || err.response?.data?.title || 'Failed to save entry')
      console.error(err)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Layout>
      <div>
        <div className="page-header">
          <h1 className="h1-header">Conversational Dictionary</h1>
          <button className="btn btn-sm btn-primary" onClick={openAdd}>+ Add Entry</button>
        </div>

        <div className="dict-filter-bar">
          <label>Filter by Language:</label>
          <select
            value={filterLanguage}
            onChange={e => setFilterLanguage(e.target.value)}
            className="dict-filter-select"
          >
            <option value="">All Languages</option>
            {languages.map(l => (
              <option key={l.languageCode} value={l.languageCode}>
                {l.languageName} ({l.languageCode})
              </option>
            ))}
          </select>
          <label>Domain:</label>
          <input
            value={filterDomain}
            onChange={e => setFilterDomain(e.target.value)}
            className="dict-filter-select"
            placeholder="All / general / meeting"
          />
        </div>

        {error && <div className="error-message">{error}</div>}

        {loading ? (
          <div>Loading entries...</div>
        ) : (
          <div className="meeting-list">
            {entries.length === 0 ? (
              <div className="dict-empty">No entries found. Click &quot;+ Add Entry&quot; to get started.</div>
            ) : (
              <table className="meeting-table">
                <thead>
                  <tr>
                    <th>Language</th>
                    <th>Domain</th>
                    <th>Formal Text</th>
                    <th>Conversational Text</th>
                    <th>Match Mode</th>
                    <th>Active</th>
                    <th>Updated By</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {entries.map(entry => (
                    <tr key={entry.id}>
                      <td><span className="dict-badge">{entry.languageCode}</span></td>
                      <td><span className="dict-domain">{entry.domain || 'general'}</span></td>
                      <td className="dict-text">{entry.formalText}</td>
                      <td className="dict-text dict-conversational">{entry.conversationalText}</td>
                      <td><span className="dict-mode">{entry.matchMode}</span></td>
                      <td>{entry.isActive ? '✅' : '❌'}</td>
                      <td className="dict-meta">{entry.updatedBy}</td>
                      <td align="right">
                        <button className="btn btn-xsm" onClick={() => openEdit(entry)}>✏️</button>
                        &nbsp;&nbsp;
                        <button className="btn btn-xsm fnt-red" onClick={() => handleDelete(entry.id)}>🗑</button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}

        {showModal && (
          <div className="modal-overlay">
            <div className="modal">
              <h2>{editingEntry ? 'Edit Dictionary Entry' : 'Add Dictionary Entry'}</h2>
              <form onSubmit={handleSubmit} className="modal-form">
                {!editingEntry && (
                  <div className="form-group">
                    <label>Language *</label>
                    <select
                      name="languageCode"
                      value={form.languageCode}
                      onChange={handleChange}
                      required
                    >
                      <option value="">-- Select Language --</option>
                      {languages.map(l => (
                        <option key={l.languageCode} value={l.languageCode}>
                          {l.languageName} ({l.languageCode})
                        </option>
                      ))}
                    </select>
                  </div>
                )}
                {editingEntry && (
                  <div className="form-group">
                    <label>Language Code</label>
                    <input value={editingEntry.languageCode} disabled className="dict-disabled" />
                  </div>
                )}
                <div className="form-group">
                  <label>Domain</label>
                  <input
                    name="domain"
                    value={form.domain}
                    onChange={handleChange}
                    placeholder="general"
                    list="dictionary-domains"
                  />
                  <datalist id="dictionary-domains">
                    <option value="general" />
                    <option value="meeting" />
                    <option value="medical" />
                    <option value="legal" />
                    <option value="training" />
                  </datalist>
                </div>
                <div className="form-group">
                  <label>Formal Text *</label>
                  <input
                    name="formalText"
                    value={form.formalText}
                    onChange={handleChange}
                    placeholder="Text as returned by Azure Speech"
                    required
                  />
                </div>
                <div className="form-group">
                  <label>Conversational Text *</label>
                  <input
                    name="conversationalText"
                    value={form.conversationalText}
                    onChange={handleChange}
                    placeholder="Natural / conversational replacement"
                    required
                  />
                </div>
                <div className="form-group">
                  <label>Match Mode *</label>
                  <select name="matchMode" value={form.matchMode} onChange={handleChange}>
                    {MATCH_MODES.map(m => (
                      <option key={m} value={m}>{m}</option>
                    ))}
                  </select>
                </div>
                {editingEntry && (
                  <div className="form-group">
                    <label>
                      <input
                        type="checkbox"
                        name="isActive"
                        checked={form.isActive}
                        onChange={handleChange}
                      />
                      {' '}Active
                    </label>
                  </div>
                )}
                {formError && <div className="error-message">{formError}</div>}
                <div className="form-actions">
                  <button
                    type="button"
                    className="btn btn-sm btn-secondary"
                    onClick={() => setShowModal(false)}
                    disabled={saving}
                  >
                    Cancel
                  </button>
                  <button type="submit" className="btn btn-sm btn-primary" disabled={saving}>
                    {saving ? 'Saving...' : editingEntry ? 'Update' : 'Create'}
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

export default DictionaryList
