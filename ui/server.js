const express = require('express');
const cors = require('cors');
const { spawn } = require('child_process');
const path = require('path');

const app = express();

const ALLOWED_ORIGINS = ['http://localhost:5173', 'http://localhost:3000', 'http://127.0.0.1:5173'];
app.use(cors({
    origin: (origin, callback) => {
        if (!origin || ALLOWED_ORIGINS.includes(origin)) {
            callback(null, true);
        } else {
            callback(new Error('Not allowed by CORS policy.'));
        }
    }
}));

app.use(express.json());

const PORT = 3001;
const PROJECT_ROOT = path.resolve(__dirname, '..');

// Strict whitelist of permitted operational commands
const ALLOWED_ACTIONS = new Map([
    ['run-api', {
        title: 'SaaS Engine API',
        cmd: 'dotnet',
        args: ['run'],
        relativeDir: 'src/SaasEngine.Api'
    }],
    ['run-admin', {
        title: 'SaaS Engine Admin',
        cmd: 'dotnet',
        args: ['run'],
        relativeDir: 'src/SaasEngine.Admin'
    }],
    ['publish-aot', {
        title: 'Publish Native AOT',
        cmd: 'dotnet',
        args: ['publish', '-c', 'Release', '-r', 'win-x64'],
        relativeDir: 'src/SaasEngine.Api'
    }],
    ['docker-up', {
        title: 'Docker Infrastructure',
        cmd: 'docker',
        args: ['compose', 'up'],
        relativeDir: '.'
    }]
]);

// Secure endpoint to execute predefined operational actions
app.post('/api/run-command', (req, res) => {
    const { action } = req.body;

    if (!action || typeof action !== 'string') {
        return res.status(400).json({ error: 'Valid action identifier is required.' });
    }

    const task = ALLOWED_ACTIONS.get(action.toLowerCase().trim());
    if (!task) {
        return res.status(400).json({ error: 'Unknown or unauthorized action.' });
    }

    const targetDir = path.resolve(PROJECT_ROOT, task.relativeDir);

    console.log(`Launching authorized action [${action}] in [${targetDir}]`);

    try {
        // Launch safely in a new terminal window on Windows without arbitrary shell interpolation
        const commandArgs = ['/K', `title ${task.title} && cd /d "${targetDir}" && ${task.cmd} ${task.args.join(' ')}`];
        const child = spawn('cmd.exe', commandArgs, {
            cwd: targetDir,
            detached: true,
            stdio: 'ignore'
        });

        child.unref();

        return res.json({ message: `Action '${action}' launched successfully.` });
    } catch (err) {
        console.error('Execution failure:', err);
        return res.status(500).json({ error: 'Internal failure while launching action.' });
    }
});

app.listen(PORT, () => {
    console.log(`SaaS Engine Command Server running at http://localhost:${PORT}`);
    console.log(`UI API Bridge is active with strict command allowlisting.`);
});
