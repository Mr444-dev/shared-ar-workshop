'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const repositoryRoot = path.resolve(__dirname, '..', '..');
const projectVersionPath = path.join(repositoryRoot, 'UnityProject', 'ProjectSettings', 'ProjectVersion.txt');
const manifestPath = path.join(repositoryRoot, 'UnityProject', 'Packages', 'manifest.json');
const projectVersion = fs.readFileSync(projectVersionPath, 'utf8');
const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));

assert.match(projectVersion, /^m_EditorVersion:\s*6000\.3\.0f1\s*$/m, 'Unity editor version must match the documented build version.');

const requiredPackages = {
  'com.unity.inputsystem': '1.16.0',
  'com.unity.ugui': '2.0.0',
  'com.unity.xr.arfoundation': '6.3.1',
  'com.unity.xr.arcore': '6.3.1',
  'com.unity.xr.arkit': '6.3.1',
  'com.unity.xr.management': '4.5.3',
};

for (const [packageName, expectedVersion] of Object.entries(requiredPackages)) {
  assert.equal(manifest.dependencies[packageName], expectedVersion, `${packageName} must remain pinned to ${expectedVersion}.`);
}

console.log('Unity project version and required direct package pins are valid. This check does not resolve Unity packages or compile the project.');
