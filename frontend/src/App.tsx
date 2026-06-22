import { Suspense } from 'react'
import { AppRoutes } from './routes'

export default function App() {
  return (
    <Suspense fallback={<div className="p-3 small text-secondary">Loading…</div>}>
      <AppRoutes />
    </Suspense>
  )
}
