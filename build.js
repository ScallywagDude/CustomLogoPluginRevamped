#!/usr/bin/env node
/*
 * Builds and packages the Custom Logo plugin for every supported Jellyfin ABI,
 * then folds the results into the repository manifest that Jellyfin subscribes to.
 *
 *   node build.js                     build, package, update manifest.json
 *   node build.js --no-zip            compile only
 *   node build.js --tag v2.0.0        set the release tag used in sourceUrl
 *
 * The tag is also read from GITHUB_REF_NAME, so CI needs no extra wiring.
 */

const { execFileSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs-extra');
const path = require('path');
const archiver = require('archiver');

const ROOT = __dirname;
const PROJECT = path.join(ROOT, 'CustomLogoPlugin.csproj');
const ASSEMBLY = 'Jellyfin.Plugin.CustomLogo';

// GitHub coordinates. Everything user-facing is derived from these two values.
const GITHUB_OWNER = 'ScallywagDude';
const GITHUB_REPO = 'CustomLogoPluginRevamped';
const DEFAULT_BRANCH = 'main';

const REPO_URL = `https://github.com/${GITHUB_OWNER}/${GITHUB_REPO}`;
const RAW_URL = `https://raw.githubusercontent.com/${GITHUB_OWNER}/${GITHUB_REPO}/${DEFAULT_BRANCH}`;

/** The URL users paste into Dashboard -> Plugins -> Repositories. */
const MANIFEST_URL = `${RAW_URL}/manifest.json`;

const PLUGIN = {
    guid: 'b7e8dce1-44df-4b37-91a3-15fcf9f3b76a',
    name: 'Custom Logo',
    description: 'Replace the default Jellyfin logo with your own.',
    overview: 'Replaces the Jellyfin logo in the web client header and splash screen with an image you upload.',
    owner: 'ScallywagDude',
    category: 'General',
    imageUrl: `${RAW_URL}/static/icon.png`
};

// One build per supported server generation. A single assembly cannot serve both:
// 10.11 runs on .NET 9 and 12.0 on .NET 10.
//
// The 12.0 build carries the higher version on purpose. On a 12.0 server BOTH
// entries pass the targetAbi filter and Jellyfin installs the highest version
// number, so the .NET 10 build has to sort above the .NET 9 one.
//
// `tag` pins the GitHub release a build's asset lives in. Set it when the two
// builds are published under different tags; omit it to use the tag passed via
// --tag / GITHUB_REF_NAME.
const TARGETS = [
    {
        tfm: 'net9.0',
        abi: '10.11.0.0',
        version: '2.0.2.0',
        label: 'jf10.11',
        jellyfin: 'Jellyfin 10.11.x',
        tag: '2.0.2.0'
    },
    {
        tfm: 'net10.0',
        abi: '12.0.0.0',
        version: '2.1.2.0',
        label: 'jf12.0',
        // targetAbi is a minimum, so this build also covers 12.1.x, whose API
        // surface is identical to 12.0.0 for everything the plugin uses.
        jellyfin: 'Jellyfin 12.0.x / 12.1.x',
        tag: '2.1.2.0'
    }
];

const CHANGELOG = [
    'Adds favicon and start-up splash logo replacement, by patching the web client index.html',
    '(neither is reachable from branding CSS). Reversible, and re-applied after a server update.',
    'Needs write access to the web client folder; the header logo works either way.'
].join(' ');

const DIST = path.join(ROOT, 'dist');
const MANIFEST = path.join(ROOT, 'manifest.json');

const argv = process.argv.slice(2);
const zipEnabled = !argv.includes('--no-zip');

function releaseTag() {
    const i = argv.indexOf('--tag');
    if (i >= 0 && argv[i + 1]) {
        return argv[i + 1];
    }

    if (process.env.GITHUB_REF_NAME && process.env.GITHUB_REF_NAME.startsWith('v')) {
        return process.env.GITHUB_REF_NAME;
    }

    return 'v2.1.1';
}

function run(cmd, args) {
    console.log(`> ${cmd} ${args.join(' ')}`);
    execFileSync(cmd, args, { stdio: 'inherit', cwd: ROOT });
}

function zipDirectory(sourceDir, outFile) {
    return new Promise((resolve, reject) => {
        const output = fs.createWriteStream(outFile);
        const archive = archiver('zip', { zlib: { level: 9 } });
        output.on('close', resolve);
        archive.on('error', reject);
        archive.pipe(output);
        archive.directory(sourceDir, false);
        archive.finalize();
    });
}

/** Compares two dotted version strings numerically, newest first. */
function compareVersionsDesc(a, b) {
    const pa = String(a).split('.').map(Number);
    const pb = String(b).split('.').map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
        const d = (pb[i] || 0) - (pa[i] || 0);
        if (d !== 0) {
            return d;
        }
    }
    return 0;
}

/**
 * Folds the freshly built versions into the existing manifest instead of
 * replacing it, so subscribers keep seeing older releases and can roll back.
 */
function mergeManifest(newVersions) {
    let manifest = [];
    if (fs.existsSync(MANIFEST)) {
        try {
            const parsed = fs.readJsonSync(MANIFEST);
            if (Array.isArray(parsed)) {
                manifest = parsed;
            }
        } catch (err) {
            console.warn(`Existing manifest.json is not readable (${err.message}); writing a fresh one.`);
        }
    }

    let entry = manifest.find((e) => e && e.guid === PLUGIN.guid);
    if (!entry) {
        entry = { ...PLUGIN, versions: [] };
        manifest.push(entry);
    }

    // Repository metadata always tracks this file.
    Object.assign(entry, PLUGIN);
    entry.versions = Array.isArray(entry.versions) ? entry.versions : [];

    for (const v of newVersions) {
        // A rebuild of the same version+ABI replaces the old record, checksum included.
        entry.versions = entry.versions.filter(
            (old) => !(old.version === v.version && old.targetAbi === v.targetAbi)
        );
        entry.versions.push(v);
    }

    entry.versions.sort((a, b) => compareVersionsDesc(a.version, b.version));

    fs.writeJsonSync(MANIFEST, manifest, { spaces: 2 });
}

async function main() {
    try {
        execFileSync('dotnet', ['--version'], { stdio: 'pipe' });
    } catch (err) {
        console.error('The .NET SDK was not found on PATH.');
        console.error('Install the .NET 10 SDK (it can build the .NET 9 target too):');
        console.error('  https://dotnet.microsoft.com/download/dotnet/10.0');
        process.exit(1);
    }

    const tag = releaseTag();

    fs.removeSync(DIST);
    fs.ensureDirSync(DIST);

    const timestamp = new Date().toISOString().replace(/\.\d+Z$/, 'Z');
    const newVersions = [];

    for (const target of TARGETS) {
        console.log(`\n=== ${target.jellyfin}  (${target.tfm}, ABI ${target.abi}) ===`);

        const stageDir = path.join(DIST, `stage-${target.label}`);
        fs.ensureDirSync(stageDir);

        // Pass the version explicitly so the assembly and meta.json cannot drift apart.
        // Jellyfin reports the assembly version as the installed one, so a mismatch makes
        // the catalogue offer an update that can never apply.
        run('dotnet', [
            'publish', PROJECT,
            '-c', 'Release',
            '-f', target.tfm,
            '-o', stageDir,
            `-p:Version=${target.version}`,
            `-p:AssemblyVersion=${target.version}`,
            `-p:FileVersion=${target.version}`
        ]);

        // Jellyfin supplies its own assemblies; shipping copies causes load conflicts.
        for (const entry of fs.readdirSync(stageDir)) {
            if (entry !== `${ASSEMBLY}.dll`) {
                fs.removeSync(path.join(stageDir, entry));
            }
        }

        if (!fs.existsSync(path.join(stageDir, `${ASSEMBLY}.dll`))) {
            console.error(`Build produced no ${ASSEMBLY}.dll for ${target.tfm}.`);
            process.exit(1);
        }

        fs.writeJsonSync(path.join(stageDir, 'meta.json'), {
            category: PLUGIN.category,
            changelog: CHANGELOG,
            description: PLUGIN.description,
            guid: PLUGIN.guid,
            name: PLUGIN.name,
            overview: PLUGIN.overview,
            owner: PLUGIN.owner,
            targetAbi: target.abi,
            timestamp: timestamp,
            version: target.version,
            status: 'Active',
            autoUpdate: true,
            imagePath: ''
        }, { spaces: 4 });

        if (!zipEnabled) {
            console.log(`Staged at ${stageDir}`);
            continue;
        }

        const zipName = `customlogo_${target.version}_${target.label}.zip`;
        const zipPath = path.join(DIST, zipName);
        await zipDirectory(stageDir, zipPath);
        fs.removeSync(stageDir);

        // Jellyfin verifies repository downloads with an MD5 hex digest.
        const checksum = crypto.createHash('md5').update(fs.readFileSync(zipPath)).digest('hex');
        console.log(`${zipName}  md5=${checksum}`);

        newVersions.push({
            version: target.version,
            changelog: `${CHANGELOG} Build for ${target.jellyfin}.`,
            targetAbi: target.abi,
            sourceUrl: `${REPO_URL}/releases/download/${target.tag || tag}/${zipName}`,
            checksum: checksum,
            timestamp: timestamp
        });
    }

    if (!zipEnabled) {
        return;
    }

    mergeManifest(newVersions);

    console.log('\nWrote manifest.json');
    console.log('Packages:');
    for (const f of fs.readdirSync(DIST)) {
        console.log(`  dist/${f}`);
    }
    console.log('\nUpload each zip to its release:');
    for (const t of TARGETS) {
        console.log(`  customlogo_${t.version}_${t.label}.zip -> ${REPO_URL}/releases/tag/${t.tag || tag}`);
    }
    console.log(`Repository URL for users:\n  ${MANIFEST_URL}`);
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
