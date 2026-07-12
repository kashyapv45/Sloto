const express = require('express');
const cors = require('cors');
const { exec } = require('child_process');
const path = require('path');

const app = express();
app.use(cors());
app.use(express.json());

const PORT = 3001;

// Base project path
const PROJECT_ROOT = path.resolve(__dirname, '..');

// Endpoint to execute a command in a new CMD window
app.post('/api/run-command', (req, res) => {
    const { command, directory } = req.body;

    if (!command) {
        return res.status(400).json({ error: 'Command is required' });
    }

    // Resolve target directory relative to project root
    const targetDir = directory ? path.resolve(PROJECT_ROOT, directory) : PROJECT_ROOT;

    console.log(`Executing command: [${command}] in [${targetDir}]`);

    // On Windows, 'start cmd.exe /K' opens a new terminal window that stays open
    // 'cd /d' handles drive letter changes securely
    const cmdStr = `start cmd.exe /K "cd /d \"${targetDir}\" && title ${command} && echo Running: ${command} && echo. && ${command}"`;

    exec(cmdStr, { cwd: targetDir }, (error) => {
        if (error) {
            console.error(`Execution error: ${error}`);
            return res.status(500).json({ error: error.message });
        }
        res.json({ message: 'Command launched successfully in a new window.' });
    });
});

app.listen(PORT, () => {
    console.log(`SaaS Engine Command Server running at http://localhost:${PORT}`);
    console.log(`UI API Bridge is active.`);
});
