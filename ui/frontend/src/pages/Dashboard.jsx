import React, { useState } from 'react';
import { LayoutDashboard, Plus, Trash2, Key } from 'lucide-react';

export default function Dashboard() {
  const [tenants, setTenants] = useState([]);
  const [name, setName] = useState('');
  const [tier, setTier] = useState('standard');
  const [planKey, setPlanKey] = useState('startup_monthly');
  const [loading, setLoading] = useState(false);

  const createWorkspace = async (e) => {
    e.preventDefault();
    setLoading(true);
    try {
      const response = await fetch('http://localhost:8080/admin/tenants/', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, tier, planKey, connectionSecretRef: 'default' })
      });
      if (!response.ok) {
        throw new Error(`API Error: ${response.status}`);
      }
      const data = await response.json();
      setTenants([...tenants, data]);
      setName('');
    } catch (err) {
      alert(`Failed to create workspace! Make sure the API is running on port 8080. Error: ${err.message}`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h2><LayoutDashboard size={24} style={{ display: 'inline', marginRight: '10px' }} /> Workspace Management</h2>
      <p className="mb-2">Provision new isolated workspaces (tenants) and manage their subscription tiers.</p>
      
      <div className="grid-2">
        <div className="glass-card">
          <h3>Create New Workspace</h3>
          <form onSubmit={createWorkspace} className="mt-2">
            <div className="form-group">
              <label>Workspace Name</label>
              <input type="text" className="form-control" value={name} onChange={e => setName(e.target.value)} required placeholder="Acme Corp" />
            </div>
            <div className="form-group">
              <label>Subscription Tier</label>
              <select className="form-control" value={tier} onChange={e => setTier(e.target.value)}>
                <option value="standard">Standard</option>
                <option value="professional">Professional</option>
                <option value="enterprise">Enterprise</option>
              </select>
            </div>
            <div className="form-group">
              <label>Plan Key</label>
              <select className="form-control" value={planKey} onChange={e => setPlanKey(e.target.value)}>
                <option value="startup_monthly">Startup Monthly</option>
                <option value="startup_yearly">Startup Yearly</option>
                <option value="pro_monthly">Pro Monthly</option>
              </select>
            </div>
            <button type="submit" className="btn btn-primary" disabled={loading}>
              <Plus size={18} /> {loading ? 'Provisioning...' : 'Provision Workspace'}
            </button>
          </form>
        </div>

        <div className="glass-card">
          <h3>Active Workspaces</h3>
          <div className="mt-2 table-container">
            {tenants.length === 0 ? (
              <p>No workspaces provisioned yet. Create one on the left!</p>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Tier</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {tenants.map(t => (
                    <tr key={t.id}>
                      <td style={{fontWeight: 500}}>{t.name}</td>
                      <td><span className="badge badge-active">{t.tier}</span></td>
                      <td><span className="badge badge-active">{t.status}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
