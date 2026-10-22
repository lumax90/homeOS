import React from 'react';
import ReactDOM from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MotionConfig } from 'motion/react';
import App from './App';
import './styles.css';
import './product.css';
import './components/home-controls.css';
import './controller/controller.css';
import { ControllerProvider } from './controller/react';
import { appUrl } from './base';

if (import.meta.env.PROD && 'serviceWorker' in navigator && window.isSecureContext) {
  window.addEventListener('load', () => { navigator.serviceWorker.register(appUrl('/sw.js'), { scope: appUrl('/'), updateViaCache: 'none' }).catch(() => { /* Normal web operation remains available. */ }); });
}

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 15_000, retry: 1, refetchOnWindowFocus: true } },
});

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <MotionConfig reducedMotion="user" transition={{ type: 'spring', stiffness: 350, damping: 34 }}>
        <ControllerProvider><App /></ControllerProvider>
      </MotionConfig>
    </QueryClientProvider>
  </React.StrictMode>,
);
