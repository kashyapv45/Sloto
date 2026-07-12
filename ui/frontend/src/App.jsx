import React from 'react'
import { BrowserRouter, Routes, Route, NavLink } from 'react-router-dom'
import { LayoutDashboard, Terminal, BookOpen, Hexagon } from 'lucide-react'
import Dashboard from './pages/Dashboard'
import CommandLauncher from './pages/CommandLauncher'
import Docs from './pages/Docs'

function App() {
  return (
    <BrowserRouter>
      <div className="app-container">
        <nav className="sidebar">
          <div className="logo">
            <Hexagon size={28} />
            <span>SaaS Engine</span>
          </div>
          <div className="nav-links">
            <NavLink to="/" className={({isActive}) => isActive ? "nav-link active" : "nav-link"} end>
              <LayoutDashboard size={20} />
              Dashboard
            </NavLink>
            <NavLink to="/launcher" className={({isActive}) => isActive ? "nav-link active" : "nav-link"}>
              <Terminal size={20} />
              Command Launcher
            </NavLink>
            <NavLink to="/docs" className={({isActive}) => isActive ? "nav-link active" : "nav-link"}>
              <BookOpen size={20} />
              Documentation
            </NavLink>
          </div>
        </nav>
        <main className="main-content">
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/launcher" element={<CommandLauncher />} />
            <Route path="/docs" element={<Docs />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  )
}

export default App
