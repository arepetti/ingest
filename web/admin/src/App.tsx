import { Navigate, Route, Routes } from 'react-router-dom'
import { Login } from './pages/Login'
import { Shell } from './pages/Shell'
import { Dashboard } from './pages/Dashboard'
import { SchemasPage } from './pages/SchemasPage'
import { SchemaEditPage } from './pages/SchemaEditPage'
import { SchemaHistoryPage } from './pages/SchemaHistoryPage'
import { SchemaVersionHistoryPage } from './pages/SchemaVersionHistoryPage'
import { ServicesPage } from './pages/ServicesPage'
import { ServiceStatusPage } from './pages/ServiceStatusPage'
import { SubmissionsPage } from './pages/SubmissionsPage'
import { MissingSubmissionsPage } from './pages/MissingSubmissionsPage'
import { ExplorePage } from './pages/ExplorePage'
import { SubmissionDetailPage } from './pages/SubmissionDetailPage'
import { SubmissionEditPage } from './pages/SubmissionEditPage'
import { ReportsPage } from './pages/ReportsPage'
import { ReportViewPage } from './pages/ReportViewPage'
import { AuditPage } from './pages/AuditPage'
import { EventsPage } from './pages/EventsPage'
import { SettingsPage } from './pages/SettingsPage'
import { ToolsPage } from './pages/ToolsPage'
import { SearchPage } from './pages/SearchPage'
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
        <Route path="/schemas/new" element={<SchemaEditPage />} />
        <Route path="/schemas/:name/edit" element={<SchemaEditPage />} />
        <Route path="/schemas/:name/history" element={<SchemaHistoryPage />} />
        <Route path="/schemas/:name/versions" element={<SchemaVersionHistoryPage />} />
        <Route path="/schemas/:name/versions/:entryId" element={<SchemaEditPage readOnly />} />
        <Route path="/services" element={<ServicesPage />} />
        <Route path="/services/:name/status" element={<ServiceStatusPage />} />
        <Route path="/submissions" element={<SubmissionsPage />} />
        <Route path="/missing" element={<MissingSubmissionsPage />} />
        <Route path="/explore" element={<ExplorePage />} />
        <Route path="/submissions/new" element={<SubmissionEditPage />} />
        <Route path="/submissions/:id/edit" element={<SubmissionEditPage />} />
        <Route path="/submissions/:id/view" element={<SubmissionEditPage readOnly />} />
        <Route path="/submissions/:id" element={<SubmissionDetailPage />} />
        <Route path="/reports" element={<ReportsPage />} />
        <Route path="/reports/:name" element={<ReportViewPage />} />
        <Route path="/audit" element={<AuditPage />} />
        <Route path="/events" element={<EventsPage />} />
        <Route path="/tools" element={<ToolsPage />} />
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="/search" element={<SearchPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
