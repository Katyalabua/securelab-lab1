const $ = (id) => document.getElementById(id);

function formData(form) {
  return Object.fromEntries(new FormData(form).entries());
}

function sendJson(method, url, body) {
  return fetch(url, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined
  });
}

function clearProfile() {
  $('profile').textContent = 'Не автентифіковано';
}

async function refreshProfile() {
  const response = await fetch('/api/me');
  $('profile-status').textContent = `GET /api/me: ${response.status}`;
  if (response.status === 401) { clearProfile(); return; }
  const type = response.headers.get('content-type') || '';
  if (!response.ok || !type.includes('application/json')) {
    clearProfile();
    $('profile-status').textContent += ' (неочікувана відповідь)';
    return;
  }
  const me = await response.json();
  $('profile').textContent = `userName: ${me.userName}\nrole: ${me.role}\nuserId: ${me.userId}`;
}

$('register-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const form = event.target;
  const response = await sendJson('POST', '/api/auth/register', formData(form));
  $('register-status').textContent = `Register: ${response.status}`;
  form.elements.password.value = '';
  await refreshProfile();   // реєстрація НЕ логінить: очікуємо 401
});

$('login-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const form = event.target;
  const response = await sendJson('POST', '/api/auth/login', formData(form));
  $('login-status').textContent = `Login: ${response.status}`;
  form.elements.password.value = '';
  await refreshProfile();
});

$('incident-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const data = formData(event.target);
  const response = await sendJson('POST', '/api/incidents', {
    title: data.title,
    description: data.description,
    severity: data.severity,
    occurredAtUtc: new Date(Date.now() - 60000).toISOString()
  });
  $('incident-status').textContent = `Create: ${response.status}`;
  if (response.status === 201) {
    const created = await response.json();
    $('incident-status').textContent += ` (id ${created.id})`;
  }
});

$('logout-btn').addEventListener('click', async () => {
  const response = await sendJson('POST', '/api/auth/logout');
  $('logout-status').textContent = `Logout: ${response.status}`;
  clearProfile();
  await refreshProfile();   // очікуємо 401
});