import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './styles.css';
import './widescreen.css';
import './incidents.css';
import './incident-focus.css';
import './everyday.css';
import './guidance.css';
import './product-controls.css';
import './brand.css';
import './release-polish.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
