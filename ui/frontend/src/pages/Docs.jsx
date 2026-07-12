import React from 'react';
import { BookOpen } from 'lucide-react';

export default function Docs() {
  return (
    <div style={{ maxWidth: '800px' }}>
      <h2><BookOpen size={24} style={{ display: 'inline', marginRight: '10px' }} /> Documentation</h2>
      
      <div className="glass-card mb-2">
        <h3>Architecture Overview</h3>
        <p className="mt-2">The SaaS Engine is a powerful, multitenant .NET 9 API utilizing Native AOT. It uses <strong>Entity Framework Core</strong> (via Dapper for high-perf reads) and stores data in <strong>PostgreSQL</strong>. Background jobs are handled by <strong>Hangfire</strong>, and telemetry is shipped via <strong>OpenTelemetry</strong>.</p>
      </div>

      <div className="glass-card mb-2">
        <h3>Workspaces vs Tenants</h3>
        <p className="mt-2">In this engine, a "Workspace" is architecturally referred to as a <strong>Tenant</strong>. Every tenant is completely isolated. When you create a workspace via the Dashboard, it sends a POST request to <code>/admin/tenants/</code>.</p>
      </div>

      <div className="glass-card mb-2">
        <h3>Subscriptions & Plans</h3>
        <p className="mt-2">The engine enforces features dynamically. Tenants are assigned to a tier (Standard, Professional, Enterprise). The <code>PlanPolicyService</code> reads the current tenant's tier and blocks requests to features they haven't paid for.</p>
      </div>

      <div className="glass-card">
        <h3>API Reference</h3>
        <p className="mt-2">
          The full OpenAPI specification and interactive Scalar UI is available directly from the engine itself.<br/><br/>
          <strong>Access the API Docs:</strong> <a href="http://localhost:8080/scalar/v1" target="_blank" style={{color: 'var(--accent-blue)'}}>http://localhost:8080/scalar/v1</a>
        </p>
      </div>
    </div>
  );
}
