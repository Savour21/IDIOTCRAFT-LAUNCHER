const icons = () => window.lucide?.createIcons?.();
const instanceCards = [...document.querySelectorAll('.instance-card')];
const selectedName = document.querySelector('#selected-name');
const selectedVersion = document.querySelector('#selected-version');
const selectedLoader = document.querySelector('#selected-loader');
const launchButton = document.querySelector('#launch-button');
const toast = document.querySelector('#toast');
const stats = JSON.parse(localStorage.getItem('idiotcord-stats') || '{"minutes":0,"mods":0,"downloads":0}');
let nativeStorageReported = false;
let accountState = { status: 'signed-out', name: '', message: 'Sign in to connect your Minecraft account.' };
const appVersion = '0.1.2';
let requiredUpdate = false;

window.addEventListener('load', () => {
  window.setTimeout(() => document.body.classList.add('ready'), 1600);
});

function nativeBridge() {
  return window.chrome?.webview?.hostObjects?.sync?.bridge || window.external;
}

function hasNativeMethod(method) {
  const bridge = nativeBridge();
  return bridge && typeof bridge[method] === 'function';
}

function formatStorage(bytes) {
  return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} GB`;
}

window.setStorageStats = (totalBytes, freeBytes, source = 'disk') => {
  nativeStorageReported = source === 'disk';
  const usedBytes = Math.max(totalBytes - freeBytes, 0);
  const percent = totalBytes ? Math.min(Math.round((usedBytes / totalBytes) * 100), 100) : 0;
  document.querySelector('#storage-percent').textContent = `${percent}%`;
  document.querySelector('#storage-meter-fill').style.width = `${percent}%`;
  document.querySelector('#storage-detail').textContent = `${formatStorage(usedBytes)} of ${formatStorage(totalBytes)} used${source === 'browser' ? ' by browser data' : ''}`;
};

if (navigator.storage?.estimate) {
  navigator.storage.estimate().then(() => {
    if (!nativeStorageReported) {
      document.querySelector('#storage-percent').textContent = 'Native app';
      document.querySelector('#storage-meter-fill').style.width = '0%';
      document.querySelector('#storage-detail').textContent = 'Open IDIOTCORD LAUNCHER.exe for disk storage';
    }
  });
}

function formatBytes(bytes) {
  if (!bytes) return '0 B';
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function renderStats() {
  document.querySelector('#play-time-stat').textContent = `${Math.floor(stats.minutes / 60)}h ${String(stats.minutes % 60).padStart(2, '0')}m`;
  document.querySelector('#mods-stat').textContent = `${stats.mods} mods`;
  document.querySelector('#downloads-stat').textContent = formatBytes(stats.downloads);
}

function nativeCall(method, ...args) {
  const bridge = nativeBridge();
  if (hasNativeMethod(method)) { bridge[method](...args); return true; }
  toast.querySelector('span').textContent = 'Microsoft login is available in IDIOTCORD LAUNCHER.exe.';
  toast.classList.add('show');
  setTimeout(() => toast.classList.remove('show'), 3200);
  return false;
}

function accountInitial(name) { return (name || 'M').trim().charAt(0).toUpperCase(); }
function renderAccount() {
  const signedIn = accountState.status === 'signed-in';
  document.querySelector('#account-name').textContent = signedIn ? accountState.name : 'Connect account';
  document.querySelector('#account-provider').textContent = signedIn ? 'Microsoft account' : 'Minecraft Java Edition';
  document.querySelectorAll('.avatar').forEach((avatar) => { avatar.textContent = accountInitial(signedIn ? accountState.name : 'M'); });
  if (!document.querySelector('#dynamic-view').hidden && document.querySelector('.nav-item.active')?.dataset.view === 'Accounts') showView('Accounts');
}

window.setAccountLoginState = (status, message, profileName = '') => {
  accountState = { status, message, name: profileName || accountState.name };
  if (status === 'signed-out' || status === 'error') accountState.name = '';
  renderAccount();
  if (status === 'error') {
    toast.querySelector('span').textContent = message;
    toast.classList.add('show');
    setTimeout(() => toast.classList.remove('show'), 6000);
  }
};

window.setLaunchState = (status, message) => {
  toast.querySelector('span').textContent = message;
  toast.classList.add('show');
  setTimeout(() => toast.classList.remove('show'), status === 'error' || status === 'crashed' ? 9000 : 3500);
};

window.setUpdateNotice = (status, version, downloadUrl, notes, updateType = 'patch') => {
  const title = document.querySelector('#release-title');
  const copy = document.querySelector('#release-copy');
  const button = document.querySelector('#download-button');
  if (status === 'available') {
    title.textContent = `Version ${version} available`;
    copy.textContent = notes || 'A new IDIOTCORD LAUNCHER release is ready to download.';
    button.href = downloadUrl || '#';
    button.classList.add('download-ready');
    document.querySelector('[data-view="News"] em').textContent = '1';
    requiredUpdate = updateType === 'required';
    launchButton.disabled = requiredUpdate;
    document.querySelector('#update-modal-title').textContent = requiredUpdate ? 'Update required to continue' : 'A new version is available';
    document.querySelector('#update-eyebrow').textContent = requiredUpdate ? 'Required launcher update' : 'Optional launcher update';
    document.querySelector('#update-modal-copy').textContent = requiredUpdate ? 'This version can no longer safely launch the game. Update before continuing.' : 'You can keep using this version, or install the latest release now.';
    document.querySelector('#update-version').textContent = `Version ${version}`;
    document.querySelector('#update-severity').textContent = updateType === 'required' ? 'Required' : `${updateType[0].toUpperCase()}${updateType.slice(1)} update`;
    const updateNow = document.querySelector('#update-now');
    updateNow.href = downloadUrl || '#';
    document.querySelector('#continue-update').hidden = requiredUpdate;
    document.querySelector('#update-modal-backdrop').classList.add('open');
  } else {
    title.textContent = `Version ${appVersion}`;
    copy.textContent = 'You are running the latest configured release.';
    button.removeAttribute('href');
    button.classList.remove('download-ready');
    requiredUpdate = false;
    launchButton.disabled = false;
  }
};

document.querySelector('#continue-update').addEventListener('click', () => document.querySelector('#update-modal-backdrop').classList.remove('open'));

window.setMinecraftVersions = (serializedVersions) => {
  const select = document.querySelector('#minecraft-version');
  const versions = serializedVersions.split('|').filter(Boolean);
  select.innerHTML = versions.map((version) => `<option value="${version}">${version}</option>`).join('');
};

renderStats();

function selectInstance(card) {
  instanceCards.forEach((item) => item.classList.remove('selected'));
  card.classList.add('selected');
  selectedName.textContent = card.dataset.name;
  selectedVersion.textContent = card.dataset.version;
  selectedLoader.textContent = card.dataset.loader;
}

instanceCards.forEach((card) => card.addEventListener('click', () => selectInstance(card)));

launchButton.addEventListener('click', () => {
  if (launchButton.disabled) return;
  const name = selectedName.textContent;
  const version = selectedVersion.textContent;
  const loader = selectedLoader.textContent === 'Vanilla' ? 'Vanilla' : 'Fabric';
  if (hasNativeMethod('launchSelected')) {
    nativeCall('launchSelected', name, version, loader);
    return;
  }
  window.setLaunchState('error', 'Open IDIOTCORD LAUNCHER.exe to install and launch Minecraft.');
});

const searchInput = document.querySelector('#search-input');
searchInput.addEventListener('input', (event) => {
  const query = event.target.value.toLowerCase().trim();
  instanceCards.forEach((card) => {
    card.style.display = card.dataset.name.toLowerCase().includes(query) ? '' : 'none';
  });
});

const dynamicView = document.querySelector('#dynamic-view');
const viewContent = {
  Instances: '<div class="view-header"><div><p class="eyebrow">Workspace</p><h2>All instances</h2><p>Manage your Minecraft profiles and loaders.</p></div><button class="launch-button" onclick="document.querySelector(\'#add-instance\').click()"><i data-lucide="plus"></i>New instance</button></div><div class="view-list">' + instanceCards.map((card) => `<div class="view-list-row"><div class="instance-art art-${card.dataset.color}"><i data-lucide="box"></i></div><div><strong>${card.dataset.name}</strong><span>Minecraft ${card.dataset.version} · ${card.dataset.loader}</span></div><button class="secondary-button" onclick="document.querySelector('[data-name=\"${card.dataset.name}\"]')?.click(); document.querySelector('[data-view=\"Overview\"]').click()">Select</button></div>`).join('') + '</div>',
  Discover: '<div class="view-header"><div><p class="eyebrow">Mod library</p><h2>Discover</h2><p>Browse modpacks and mods once the native catalog connection is configured.</p></div><div class="empty-symbol"><i data-lucide="compass"></i></div></div><div class="empty-view"><i data-lucide="package-search"></i><strong>Catalog connection coming next</strong><span>Connect Modrinth or CurseForge from the desktop services layer.</span></div>',
  News: () => `<div class="view-header"><div><p class="eyebrow">IDIOTCORD updates</p><h2>News</h2><p>Launcher updates and Minecraft community notes.</p></div></div><div class="news-list"><article><span>${appVersion}</span><div><strong>Authentication and updater fixes</strong><p>Microsoft login configuration, public GitHub update metadata, and startup update decisions are now aligned.</p></div></article><article><span>${appVersion}</span><div><strong>Launcher polish</strong><p>Added the animated startup splash, CSS voxel block mark, and synchronized release notes.</p></div></article><article><span>0.1.0</span><div><strong>Launcher foundation</strong><p>Initial native host, WebView2 dashboard, Minecraft version discovery, and vanilla installation foundation.</p></div></article></div>`,
  Accounts: () => { const signedIn = accountState.status === 'signed-in'; const busy = accountState.status === 'working'; return `<div class="view-header"><div><p class="eyebrow">Identity</p><h2>Accounts</h2><p>Connect a Microsoft account to verify Minecraft: Java Edition ownership.</p></div><button class="launch-button" onclick="nativeCall('${signedIn ? 'microsoftLogout' : 'microsoftLogin'}')" ${busy ? 'disabled' : ''}><i data-lucide="${signedIn ? 'log-out' : busy ? 'loader-circle' : 'log-in'}"></i>${signedIn ? 'Sign out' : busy ? 'Signing in…' : 'Connect account'}</button></div><div class="account-card"><div class="avatar">${accountInitial(signedIn ? accountState.name : 'M')}</div><div><strong>${signedIn ? accountState.name : 'Microsoft account'}</strong><span>${accountState.message}</span></div><i data-lucide="${signedIn ? 'shield-check' : 'shield'}"></i></div>`; },
  Settings: '<div class="view-header"><div><p class="eyebrow">Preferences</p><h2>Settings</h2><p>Configure the launcher before connecting native services.</p></div></div><div class="settings-list"><label>Game directory<span>Not configured</span></label><label>Java runtime<span>Detected by desktop host</span></label><label>Microsoft client ID<span>Required for account login</span></label></div>'
};

function showView(view) {
  const isOverview = view === 'Overview';
  document.querySelector('.content-wrap').hidden = !isOverview;
  dynamicView.hidden = isOverview;
  if (!isOverview) {
    const content = viewContent[view] || viewContent.Instances;
    dynamicView.innerHTML = typeof content === 'function' ? content() : content;
    icons();
  }
}

document.querySelectorAll('.nav-item').forEach((item) => item.addEventListener('click', () => {
  document.querySelectorAll('.nav-item').forEach((nav) => nav.classList.remove('active'));
  item.classList.add('active');
  document.querySelector('#view-title').textContent = item.dataset.view;
  showView(item.dataset.view);
}));

document.querySelector('#account-button').addEventListener('click', () => {
  if (accountState.status === 'signed-in') document.querySelector('[data-view="Accounts"]').click();
  else nativeCall('microsoftLogin');
});

const modalBackdrop = document.querySelector('#modal-backdrop');
const openModal = () => modalBackdrop.classList.add('open');
const closeModal = () => modalBackdrop.classList.remove('open');
document.querySelector('#add-instance').addEventListener('click', openModal);
document.querySelector('#modal-close').addEventListener('click', closeModal);
modalBackdrop.addEventListener('click', (event) => { if (event.target === modalBackdrop) closeModal(); });
document.querySelector('#create-instance').addEventListener('click', () => {
  const name = document.querySelector('#new-instance-name').value.trim() || 'My new world';
  const version = document.querySelector('#minecraft-version').value;
  const newCard = document.createElement('button');
  newCard.className = 'instance-card';
  newCard.dataset.name = name;
  newCard.dataset.version = version;
  newCard.dataset.loader = '0.16.9';
  newCard.innerHTML = '<span class="instance-art art-mint"><i data-lucide="sparkles"></i></span><span class="instance-card-info"><strong></strong><span></span></span><span class="card-menu"><i data-lucide="more-horizontal"></i></span><span class="play-indicator"><i data-lucide="play"></i></span>';
  newCard.querySelector('strong').textContent = name;
  newCard.querySelector('.instance-card-info span').textContent = `${version} · Vanilla`;
  newCard.addEventListener('click', () => selectInstance(newCard));
  document.querySelector('#instance-grid').prepend(newCard);
  instanceCards.push(newCard);
  selectInstance(newCard);
  if (hasNativeMethod('installVanilla')) nativeCall('installVanilla', name, version);
  closeModal();
  toast.querySelector('span').textContent = `${name} added to your library`;
  toast.classList.add('show');
  setTimeout(() => toast.classList.remove('show'), 2800);
  icons();
});

document.querySelector('#view-all').addEventListener('click', () => {
  document.querySelector('[data-view="Instances"]').click();
  document.querySelector('#view-title').textContent = 'Instances';
  document.querySelector('.instances-section').scrollIntoView({ behavior: 'smooth' });
});

icons();
renderAccount();
if (hasNativeMethod('getMinecraftVersions')) nativeCall('getMinecraftVersions');
if (hasNativeMethod('checkForUpdates')) nativeCall('checkForUpdates');
