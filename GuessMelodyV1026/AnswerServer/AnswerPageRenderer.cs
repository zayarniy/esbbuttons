namespace GuessMelody.AnswerServer
{
    public static class AnswerPageRenderer
    {
        public static string Render(AnswerHttpServer srv)
        {
            var title = Html(srv.Publisher?.Title ?? "Угадай мелодию");
            var port = srv.Port;

            return $@"<!DOCTYPE html>
<html lang=""ru"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1,user-scalable=no"">
<title>{title}</title>
<style>
  * {{ box-sizing: border-box; }}
  body {{
    font-family: -apple-system, Segoe UI, Roboto, Arial, sans-serif;
    background:#161625; color:#eee; margin:0; padding:12px;
    -webkit-tap-highlight-color: transparent;
  }}
  h1 {{ margin:0 0 4px; font-size:22px; }}
  .muted {{ color:#8888a0; font-size:14px; }}
  .card {{
    background:#23233a; padding:14px 16px; border-radius:12px;
    margin-bottom:10px;
  }}
  .row {{ display:flex; justify-content:space-between; align-items:center; gap:8px; }}
  .state {{ font-size:22px; font-weight:600; }}
  .round {{ font-size:14px; color:#ffd166; }}
  .cat {{ font-size:16px; margin-top:4px; }}
  .track {{ font-size:12px; color:#8a8aa0; margin-top:4px; word-break:break-all; }}
  .first {{ margin-top:8px; padding:8px 12px; background:#3a1f1f; color:#ffb3b3;
           border-radius:8px; font-size:16px; font-weight:600; }}
  .first.none {{ background:#23233a; color:#666; font-weight:400; }}
  .bar {{ height:8px; background:#333; border-radius:4px; overflow:hidden; margin-top:8px; }}
  .bar > div {{ height:100%; background:#ffd166; width:0%; transition: width 0.2s linear; }}
  .btnrow {{ display:flex; gap:10px; margin:12px 0; flex-wrap:wrap; }}
  button {{
    flex:1 1 40%; min-height:80px; font-size:22px; font-weight:600;
    color:#fff; border:none; border-radius:12px; cursor:pointer;
    padding:14px 8px; transition: transform 0.05s, opacity 0.2s;
  }}
  button:active {{ transform: scale(0.97); opacity:0.9; }}
  .yes {{ background:#2e7d32; }}
  .no  {{ background:#c62828; }}
  .next {{ background:#1565c0; font-size:18px; min-height:60px; }}
  .panic {{ background:#6d1b1b; font-size:16px; min-height:50px; }}
  .teams {{ display:grid; grid-template-columns: 1fr 1fr; gap:8px; margin-top:6px; }}
  .team {{ background:#2b2b44; padding:10px 12px; border-radius:10px; }}
  .team.first {{ background:#3d2d10; border:2px solid #ffd166; }}
  .team-name {{ font-size:14px; color:#aaa; }}
  .team-score {{ font-size:26px; font-weight:700; color:#ffd166; }}
  .cats {{ margin-top:8px; }}
  .catrow {{ display:flex; justify-content:space-between; font-size:13px; padding:2px 0; }}
  .catrow.active {{ color:#ffd166; font-weight:600; }}
  .footer {{ text-align:center; color:#555; font-size:12px; margin:12px 0 24px; }}
  .err {{ color:#ff7070; font-size:12px; margin-top:4px; }}
</style>
</head>
<body>

<h1 id=""title"">{title}</h1>
<div class=""muted"" id=""gameName""></div>

<div class=""card"">
  <div class=""row"">
    <div class=""state"" id=""state"">—</div>
    <div class=""round"" id=""round""></div>
  </div>
  <div class=""cat"" id=""cat""></div>
  <div class=""track"" id=""track""></div>
  <div class=""bar""><div id=""bar"" style=""width:0%""></div></div>
  <div class=""first none"" id=""first"">Никто ещё не нажимал</div>
  <div id=""placeholder"" class=""muted"" style=""margin-top:8px""></div>
</div>

<div class=""btnrow"">
  <button class=""yes""   onclick=""act('score/yes')"">✅ Да</button>
  <button class=""no""    onclick=""act('score/no')"">❌ Нет</button>
</div>
<div class=""btnrow"">
  <button class=""next""  onclick=""act('next')"">⏭ Следующий</button>
  <button class=""panic"" onclick=""act('panic')"">🚨 PANIC</button>
</div>

<div class=""card"">
  <div class=""muted"">Очки</div>
  <div class=""teams"" id=""teams""></div>
</div>

<div class=""card"">
  <div class=""muted"">Категории</div>
  <div class=""cats"" id=""cats""></div>
</div>

<div class=""footer"">Сервер ответов, порт {port}. Страница обновляется автоматически.</div>
<div class=""footer"" id=""err""></div>

<script>
let lastRevision = -1;
let failCount = 0;

function esc(s) {{
  if (s === null || s === undefined) return '';
  return String(s).replace(/[&<>""']/g, c => ({{
    '&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;',""'"":'&#39;'
  }})[c]);
}}

async function fetchState() {{
  try {{
    const r = await fetch('state', {{ cache: 'no-store' }});
    if (!r.ok) throw new Error('HTTP ' + r.status);
    const s = await r.json();
    failCount = 0;
    document.getElementById('err').textContent = '';
    render(s);
  }} catch (e) {{
    failCount++;
    document.getElementById('err').textContent =
      'Ошибка связи с сервером (' + failCount + '): ' + e.message;
  }}
}}

function render(s) {{
  document.getElementById('title').textContent = s.title || '';
  document.getElementById('gameName').textContent = s.gameName || '';
  document.getElementById('state').textContent = s.stateText || '';
  document.getElementById('round').textContent = s.roundText || '';
  document.getElementById('cat').textContent = s.categoryText && s.categoryText !== '—'
      ? 'Категория: ' + s.categoryText : '';
  document.getElementById('track').textContent = s.trackText && s.trackText !== '—'
      ? 'Трек: ' + s.trackText : '';

  // прогресс
  let pct = 0;
  if (s.trackTotalSec > 0) pct = Math.max(0, Math.min(100, s.trackLeftSec / s.trackTotalSec * 100));
  document.getElementById('bar').style.width = pct.toFixed(1) + '%';

  // первый
  const first = document.getElementById('first');
  if (s.firstPressedText && s.firstPressedText !== '—') {{
    first.textContent = '🎯 Первый: ' + s.firstPressedText;
    first.className = 'first';
  }} else {{
    first.textContent = 'Никто ещё не нажимал';
    first.className = 'first none';
  }}

  // placeholder
  document.getElementById('placeholder').textContent = s.placeholderText || '';

  // teams
  const teamsDiv = document.getElementById('teams');
  teamsDiv.innerHTML = '';
  (s.teams || []).forEach(t => {{
    const d = document.createElement('div');
    d.className = 'team' + (t.firstPressed ? ' first' : '');
    d.innerHTML = '<div class=""team-name"">' + esc(t.name) +
                  '</div><div class=""team-score"">' + t.score + '</div>';
    teamsDiv.appendChild(d);
  }});

  // categories
  const catsDiv = document.getElementById('cats');
  catsDiv.innerHTML = '';
  (s.categories || []).forEach(c => {{
    const d = document.createElement('div');
    d.className = 'catrow' + (c.isActive ? ' active' : '');
    d.innerHTML = '<span>' + esc(c.name) + '</span><span>' + c.remaining + ' / ' + c.total + '</span>';
    catsDiv.appendChild(d);
  }});
}}

async function act(path) {{
  try {{
    await fetch(path, {{ method: 'POST', cache: 'no-store' }});
    // сразу подтянем состояние
    setTimeout(fetchState, 50);
  }} catch (e) {{
    document.getElementById('err').textContent = 'Ошибка: ' + e.message;
  }}
}}

fetchState();
setInterval(fetchState, 1000);
</script>
</body>
</html>";
        }

        private static string Html(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}