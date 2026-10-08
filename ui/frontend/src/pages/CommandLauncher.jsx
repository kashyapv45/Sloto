import React, { useState } from 'react';
import { Terminal, Play, Loader2, CheckCircle2 } from 'lucide-react';

export default function CommandLauncher() {
  const [status, setStatus] = useState(null);

  const commands = [
    { action: 'run-api', name: 'Run API Server', cmd: 'dotnet run', dir: 'src/SaasEngine.Api', desc: 'Starts the main API engine on port 8080.' },
    { action: 'run-admin', name: 'Run Admin/Migrations', cmd: 'dotnet run', dir: 'src/SaasEngine.Admin', desc: 'Starts the Admin EF Core tooling and migration runner.' },
    { action: 'publish-aot', name: 'Publish AOT (Windows)', cmd: 'dotnet publish -c Release -r win-x64', dir: 'src/SaasEngine.Api', desc: 'Publishes a Native AOT binary optimized for Windows.' },
    { action: 'docker-up', name: 'Docker Compose Up', cmd: 'docker compose up', dir: '.', desc: 'Starts the entire SaaS engine docker infrastructure.' }
  ];

  const launchCommand = async (action) => {
    setStatus({ cmd: action, state: 'launching' });
    try {
      const response = await fetch('http://localhost:3001/api/run-command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ action })
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || 'Failed to launch');
      setStatus({ cmd: action, state: 'success' });
      setTimeout(() => setStatus(null), 3000);
    } catch (err) {
      alert(`Error launching command: ${err.message}`);
      setStatus(null);
    }
  };

  return (
    <div>
      <h2><Terminal size={24} style={{ display: 'inline', marginRight: '10px' }} /> Command Launcher</h2>
      <p className="mb-2">Click any button below to securely open a new CMD window and execute the task automatically.</p>
      
      <div className="grid-2">
        {commands.map((c, idx) => (
          <div key={idx} className="glass-card">
            <h3>{c.name}</h3>
            <p style={{ margin: '1rem 0' }}>{c.desc}</p>
            <div style={{ background: 'rgba(0,0,0,0.3)', padding: '0.75rem', borderRadius: '8px', fontFamily: 'monospace', fontSize: '0.85rem', marginBottom: '1.5rem', border: '1px solid var(--glass-border)' }}>
              &gt; cd {c.dir} <br />
              &gt; {c.cmd}
            </div>
            <button 
              className="btn btn-primary" 
              style={{ width: '100%' }}
              onClick={() => launchCommand(c.action)}
              disabled={status && status.cmd === c.action && status.state === 'launching'}
            >
              {status && status.cmd === c.action && status.state === 'launching' ? (
                <><Loader2 className="lucide-spin" size={18} /> Launching CMD...</>
              ) : status && status.cmd === c.action && status.state === 'success' ? (
                <><CheckCircle2 size={18} /> Launched!</>
              ) : (
                <><Play size={18} /> Execute Command</>
              )}
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}
