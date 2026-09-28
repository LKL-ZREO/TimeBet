import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

if (
  import.meta.env.DEV &&
  new URLSearchParams(window.location.search).get('preview') === '1'
) {
  const { installPreviewBridge } = await import('./dev/previewBridge')
  installPreviewBridge()
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
