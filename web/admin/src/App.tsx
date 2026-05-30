import { Navigate, Route, Routes } from 'react-router-dom'
import { Login } from './pages/Login'
import { Shell } from './pages/Shell'
import { Dashboard } from './pages/Dashboard'
import { SchemasPage } from './pages/SchemasPage'
import { SchemaHistoryPage } from './pages/SchemaHistoryPage'
import { ServicesPage } from './pages/ServicesPage'
import { ServiceStatusPage } from './pages/ServiceStatusPage'
import { SubmissionsPage } from './pages/SubmissionsPage'
import { SubmissionDetailPage } from './pages/SubmissionDetailPage'
import { SubmissionEditPage } from './pages/SubmissionEditPage'
import { ReportsPage } from './pages/ReportsPage'
import { ReportViewPage } from './pages/ReportViewPage'
import { RequireAuth } from './pages/RequireAuth'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route
        element={
          <RequireAuth>
            <Shell />
          </RequireAuth>
        }
      >
        <Route path="/" element={<Dashboard />} />
        <Route path="/schemas" element={<SchemasPage />} />
        <Route path="/schemas/:name/history" element={<SchemaHistoryPage />} />
        <Route path="/services" element={<ServicesPage />} />
        <Route path="/services/:name/status" element={<ServiceStatusPage />} />
        <Route path="/submissions" element={<SubmissionsPage />} />
        <Route path="/submissions/new" element={<SubmissionEditPage />} />
        <Route path="/submissions/:id/edit" element={<SubmissionEditPage />} />
        <Route path="/submissions/:id/view" element={<SubmissionEditPage readOnly />} />
        <Route path="/submissions/:id" element={<SubmissionDetailPage />} />
        <Route path="/reports" element={<ReportsPage />} />
        <Route path="/reports/:name" element={<ReportViewPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
