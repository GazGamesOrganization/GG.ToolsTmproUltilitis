# Mobile Input Preview — como o cálculo funciona e como os dados foram tirados do APK

Documento técnico. Para uso do dia a dia, ver o [README](README.md).

Duas partes:

1. **[O cálculo](#1-o-cálculo)** — como a barra decide onde ficar.
2. **[A instrumentação](#2-a-instrumentação)** — como ler o estado interno rodando num APK em device, sem debugger.

---

# 1. O cálculo

Três etapas independentes. Cada uma tem uma unidade diferente, e misturar as unidades foi a fonte da maioria dos bugs.

```
[A] altura do teclado          px reais de tela
        ↓
[B] offset desejado            px reais de tela
        ↓
[C] posição do RectTransform   unidades do canvas
```

---

## [A] Altura do teclado — `KeyboardHeightSensor.cs`

### Por que não dá pra usar `TouchScreenKeyboard.area`

É a API oficial e a resposta óbvia. Não funciona.

Medido num Galaxy Tab (`scr=1340x800`), com o teclado aberto ocupando 400px:

```
area=0
```

Zero. Não é o "rect zerado nos primeiros frames" que a documentação sugere — fica zero o tempo todo, nesse device. Um chute de percentual em cima disso é o que colocava a barra atrás do teclado.

Na WebGL é pior: não existe. Vendo o runtime do módulo WebGL do Unity —
`Editor/Data/PlaybackEngines/WebGLSupport/BuildTools/lib/MobileKeyboard.js` — as funções exportadas são
`Show`, `Hide`, `GetText`, `SetText`, `GetTextSelection`, `SetTextSelection`, `SetCharacterLimit`,
`GetKeyboardStatus`. Não há nenhum `GetRect`. Logo `area` é `Rect.zero` por construção.

### O que funciona no Android: perguntar pro framework

O Android sabe exatamente quanto da janela está coberto. A pergunta certa é `getWindowVisibleDisplayFrame`,
o mesmo método que o próprio framework usa pra decidir layout. Via JNI:

```csharp
using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
using (AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow"))
{
    decorView = window.Call<AndroidJavaObject>("getDecorView");
}

decorView.Call("getWindowVisibleDisplayFrame", visibleFrame);

int visibleBottom = visibleFrame.Get<int>("bottom");
int decorHeight   = decorView.Call<int>("getHeight");

float inset = decorHeight - visibleBottom;
```

`visibleFrame.bottom` é medido **a partir do topo** da tela. `decorHeight - bottom` é, portanto, quanto está
coberto **embaixo**.

Três cuidados no código:

**1. `decorView` e `visibleFrame` são criados uma vez e reusados.** `new AndroidJavaObject` por frame gera
lixo de GC e travessia JNI à toa. Ficam em campos, liberados no `Dispose()`.

**2. Poll a cada 0.05s, não por frame.** `GetHeightPixels()` faz cache. Chamar por frame custaria ~180
travessias JNI por segundo sem ganho: o teclado anima em ~0.25s, 20Hz cobre.

**3. Conversão decor→Screen.** O decor view e a surface do Unity podem divergir (multi-window, split screen):

```csharp
float toScreenPixels = Screen.height / (float)decorHeight;
lastRawInset = insetInDecorPixels * toScreenPixels;
```

### O baseline: separar teclado de barra de navegação

`getWindowVisibleDisplayFrame` não distingue teclado de nav bar — as duas cobrem a parte de baixo. Sem
tratar isso, um device com nav bar de 60px teria a barra sempre 60px alta demais.

Solução: medir o inset **enquanto nenhum campo está focado** — aí o que estiver embaixo só pode ser
navegação — e subtrair depois.

```csharp
// MobileInputPreview.Update(), quando !hasField, a cada 0.5s
keyboardSensor.CaptureBaseline();

// KeyboardHeightSensor.Measure()
float height = inset - baselineInset;
if (height > Screen.height * 0.08f) return height;
return 0f;   // menos que 8% da tela não é teclado
```

O corte de 8% evita que ruído de 1-2px vire "teclado aberto".

Em app fullscreen (desenhando atrás da nav bar) o baseline dá `0`, que é o correto — foi o caso do device
testado (`base=0`).

### Degradação

`Auto` resolve por plataforma, e cada nível cai pro seguinte se falhar:

```
Editor + simulateInEditor  →  Screen.height * simulatedKeyboardHeightPercent
Android                    →  JNI visible frame
   ponte JNI falhou        →  TouchScreenKeyboard.area
   area == 0               →  Screen.height * fallbackKeyboardHeightPercent
outras plataformas         →  TouchScreenKeyboard.area  →  percentual
```

O `src=` no dump mostra qual nível venceu. `src=FixedPercent` num device significa que nada mediu e o
código está chutando.

---

## [B] Offset desejado — `MobileInputPreview.GetBottomOffset()`

```csharp
float offset = keyboardSensor.GetHeightPixels() + settings.extraKeyboardMargin;

if (settings.respectSafeArea)
{
    offset = Mathf.Max(offset, Screen.safeArea.yMin);
}
```

`Screen.safeArea.yMin` é o inset de baixo — gesture bar, home indicator.

**É `Max`, não soma.** Com o teclado aberto ele já cobre o inset de baixo; somar empurraria a barra pra cima
sem motivo. O `Max` só age quando o teclado é baixo ou está fechado.

---

## [C] Posição do RectTransform — `MobileInputPreviewView.SetBottomOffset()`

Esta etapa foi reescrita duas vezes. Vale registrar por quê.

### Tentativa 1 — conversão direta (errada)

```csharp
position.y = bottomOffsetPixels / canvas.scaleFactor;
```

A divisão por `scaleFactor` é necessária e continua no código como fallback: `TouchScreenKeyboard.area` e
`Screen.height` vêm em **pixels físicos**, enquanto `anchoredPosition` vive em **unidades de referência do
canvas**. Com `CanvasScaler` em `ScaleWithScreenSize`, referência 1080x1920, num device 1440p o
`scaleFactor` é ~1.33 — sem dividir, a barra voaria 33% acima do teclado.

O que estava errado: `anchoredPosition` endereça o **pivot**, não a borda de baixo. Com `pivot.y = 0.5`, a
barra fica com o **centro** na linha do teclado, ou seja, cortada exatamente na metade.

### Tentativa 2 — compensar pivot e filhos (ainda frágil)

```csharp
float pivotCompensation = barRect.pivot.y * barRect.rect.height;
Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(barRect);
float overshoot = Mathf.Max(0f, -barRect.pivot.y * barRect.rect.height - bounds.min.y);
position.y = offsetInUnits + pivotCompensation + overshoot;
```

Corrige pivot e filhos que vazam do rect. Mas ainda assume que pivot, âncora e `rect.height` descrevem a
barra. Basta a arte estar montada de um jeito não previsto — stretch vertical, layout group, visual maior
que o rect — pra sair errado de novo. Cada caso novo virava um termo novo na fórmula.

### Tentativa 3 — medir e corrigir (atual)

Em vez de calcular a posição absoluta a partir de premissas, medir onde a barra **está** e mover pela
diferença:

```csharp
RectTransform parent = (RectTransform)barRect.parent;

// Onde o conteúdo está agora, no espaço local do pai. Inclui filhos que vazam do rect.
Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, barRect);
float currentBottomLocal = bounds.min.y;

// Onde deveria estar: a mesma altura de tela, no mesmo espaço local.
Vector2 screenPoint = new Vector2(Screen.width * 0.5f, bottomOffsetPixels);
RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, eventCamera, out Vector2 localPoint);

float delta = localPoint.y - currentBottomLocal;
barRect.anchoredPosition += new Vector2(0f, delta);
```

Por que isso é melhor:

- **Não assume nada.** Pivot, âncora, stretch, layout group, filho que vaza, `scaleFactor`, pillarbox,
  letterbox, Screen Space Overlay vs Camera — tudo já está embutido na posição medida.
- **`ScreenPointToLocalPointInRectangle` é a mesma função que o uGUI usa pra hit-testing.** O mapeamento
  tela→local é literalmente o que o Unity considera verdade, não uma reimplementação.
- **Autocorretivo.** Cada frame mede o estado real e corrige. Um frame errado se conserta no próximo. Se
  algo mais escreve `anchoredPosition`, a correção não some — e `corr=` no dump denuncia.

Fallback pra tentativa 2 quando `barRect.parent` não é um `RectTransform` (barRect é raiz de canvas) ou
quando `ScreenPointToLocalPointInRectangle` falha.

### Pillarbox / letterbox

O fallback direto desconta o viewport da câmera:

```csharp
if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
    canvasBottomPixels = canvas.worldCamera.pixelRect.yMin;
```

Um canvas Screen Space - Camera cobre só `camera.pixelRect`. Com letterbox esse viewport não começa no
fundo da tela, então a barra preta de baixo precisa ser descontada. No caminho medido isso vem de graça —
`ScreenPointToLocalPointInRectangle` já recebe a câmera.

---

# 2. A instrumentação

Como ler o estado interno de um APK rodando num tablet, sem debugger anexado.

## O problema

`Debug.Log` exige `adb logcat` com o cabo plugado. Uma UI de debug em uGUI exige montar prefab, que é
justamente o que pode estar quebrado. E medir por pixel de screenshot é chute — foi assim que perdi duas
rodadas de build.

## Camada 1 — dump de texto via IMGUI

`OnGUI` não precisa de canvas, prefab, `EventSystem` ou câmera. Roda em qualquer build, desenha por cima
de tudo, incluindo canvases Screen Space Overlay.

```csharp
private void OnGUI()
{
    if (Instance != this || !settings.showDiagnostics || diagnostics.Length == 0) return;

    DrawGeometryOverlay();

    if (view != null && view.HasDiagnosticsLabel) return;   // usa o TMP_Text se existir

    // faixa preta translúcida + GUI.Label
}
```

Três destinos, na ordem: `diagnosticsLabel` (`TMP_Text`) se estiver ligado; senão IMGUI; e sempre
`Debug.Log` 1x/s, pra `adb logcat -s Unity` quando o cabo estiver disponível.

**Detalhe que custou uma rodada de build:** na primeira versão o dump só era montado dentro do `Refresh()`,
que só roda com um campo focado e a view válida. Ou seja: não aparecia nada exatamente quando algo estava
mal configurado. Agora `UpdateDiagnostics()` é a primeira coisa do `Update()`, antes de qualquer
early-return.

### O dump

```
src=AndroidVisibleFrame kb=402 raw=402 base=0 area=0 off=402 scr=1340x800 safe=0 sf=0,70
field=1 act=1 view=1 tsk=1 bot=402 corr=0
rect=750x47 pv=0,00 anc=0,00/0,00 ap=621 kids=3 whole=1 all=402..449 own=402..449 lbl=47
```

Linha 1 — medição:

| | |
|---|---|
| `src` | Qual fonte de medição venceu. |
| `kb` | Altura do teclado em px, já sem o baseline. |
| `raw` | Inset bruto do window frame, antes do baseline. |
| `base` | Baseline (nav bar). `-1` = nunca capturado. |
| `area` | O que `TouchScreenKeyboard.area` diz, pra comparação direta. |
| `off` | Offset final pedido pra view. |
| `scr` | `Screen.width x Screen.height`. |
| `safe` | `Screen.safeArea.yMin`. |
| `sf` | `canvas.scaleFactor`. |

Linha 2 — estado:

| | |
|---|---|
| `field` | 1 = tem `TMP_InputField` focado. |
| `act` | 1 = `activationMode` permitiu. |
| `view` | 1 = view achada com as referências obrigatórias. |
| `tsk` | `TouchScreenKeyboard.isSupported`. |
| `bot` | Onde a borda de baixo estava antes da correção. |
| `corr` | Correção aplicada. Estabiliza perto de 0. |

Linha 3 — geometria do `RectTransform`:

| | |
|---|---|
| `rect` | `barRect.rect` em unidades do canvas. |
| `pv` | `barRect.pivot.y`. |
| `anc` | `anchorMin.y` / `anchorMax.y`. |
| `ap` | `anchoredPosition.y`. |
| `kids` | Número de filhos. |
| `whole` | `alignWholeContent`. |
| `all` | Bounds em px de tela, filhos inclusos. |
| `own` | Bounds do `barRect` sozinho. |
| `lbl` | Altura do `previewLabel`. |

## Camada 2 — overlay geométrico

Números só provam consistência interna. `bot=402 corr=0` parece perfeito, mas `bot` é derivado de `off` —
prova que o código convergiu, não que convergiu no lugar certo. Se os bounds apontam pro objeto errado, ele
converge satisfeito no lugar errado e **nenhum número denuncia**.

A solução foi desenhar o que o código acha, por cima dos pixels reais:

```csharp
float keyboardTop = GetBottomOffset();
DrawScreenLine(new Rect(0, Screen.height - keyboardTop - 1, Screen.width, 3), Color.red);

if (view.TryGetContentScreenRect(true,  out Rect withKids)) DrawScreenOutline(withKids, Color.green);
if (view.TryGetContentScreenRect(false, out Rect ownOnly))  DrawScreenOutline(ownOnly,  Color.yellow);
```

- **Linha vermelha** — onde o topo do teclado foi medido.
- **Caixa verde** — bounds que o código alinha, filhos inclusos.
- **Caixa amarela** — o rect do `barRect` sozinho.

Conversão de espaço, porque screen space tem y pra cima e IMGUI tem y pra baixo:

```csharp
float top = Screen.height - screenRect.yMax;
```

Tabela de leitura:

| Print mostra | Conclusão |
|---|---|
| Verde encosta na vermelha e abraça a barra | Geometria correta. |
| Verde encosta na vermelha mas fica acima da barra | Bounds não enxergam a arte — não é descendente do `barRect`. |
| Verde não encosta na vermelha | Outra coisa escreve `anchoredPosition` depois (Animator, LayoutGroup). |
| Vermelha não bate com o teclado | A medição está errada apesar dos números fecharem. |
| Nenhuma caixa aparece | `TryGetContentScreenRect` deu false — `barRect` não tem pai `RectTransform`. |

## Como o bug final foi identificado

O último dump do Editor:

```
rect=2690x1080 pv=0,50 anc=-0,00/0,00 ap=478 kids=2 whole=1 lbl=100
```

Quatro observações, uma conclusão:

1. `rect` tem **1080 unidades de altura** — a tela inteira. `lbl=100`. A "barra" era 10x maior que o
   próprio label.
2. `pv=0,50` — pivot central.
3. **`all=` e `own=` não apareceram.** Esses campos só somem quando `TryGetContentScreenRect` retorna
   false, e o único caminho pra isso é `barRect.parent` não ser um `RectTransform`.
4. Nenhuma caixa verde ou amarela no print, consistente com (3).

Um `RectTransform` de tela cheia, pivot 0.5, sem pai `RectTransform`, é a **raiz de um Canvas**.

Cadeia completa: `barRect` apontava pro Canvas → sem pai `RectTransform`, o caminho medido nunca rodava →
caía no fallback de matemática direta → movia o canvas inteiro → pivot 0.5 de um rect de 1080 punha o
centro na linha do teclado → barra cortada exatamente no meio.

Causa: o `Reset()` original fazia `barRect = transform as RectTransform`. Com a view no Canvas, ele
auto-preenchia com o próprio Canvas.

Correções: `Reset()` agora sugere o pai do `previewLabel` e nunca o próprio transform;
`BarRectProblem` valida os três casos (é um Canvas / sem pai `RectTransform` / mais alto que 70% do canvas)
e cospe `>>> ... <<<` no topo do dump mais um `Debug.LogWarning`.

## Lição

O ponto que teria economizado três rodadas de build: **a instrumentação precisa poder contradizer o
código**. As duas primeiras rodadas mediam valores que o próprio código produzia — números autoconsistentes,
sempre "corretos". Só quando o overlay passou a desenhar a interpretação do código por cima do pixel real
é que a discordância ficou visível.

---

## Referência de arquivos

```
Runtime/MobileInputPreview/
  KeyboardHeightSensor.cs      etapa [A] — JNI, baseline, degradação, BuildDiagnostics
  MobileInputPreview.cs        etapa [B] — GetBottomOffset, OnGUI, DrawGeometryOverlay
  MobileInputPreviewView.cs    etapa [C] — SetBottomOffset, TryGetContentScreenRect, BarRectProblem
  MobileInputPreviewSettings.cs
  MobileInputPreviewTarget.cs
```

---

# 3. WebGL

## Decisão: usar a barra do Unity

Na WebGL o `MobileInputPreview` se desliga em modo `Auto`. Não é limitação — é escolha.

Com `Module.SystemInfo.mobile` verdadeiro, `JS_MobileKeyboard_Show` cria isto no `document.body`:

```js
inputContainer.style = "width:100%; position:fixed; bottom:0px; margin:0px; padding:0px; left:0px; " +
                       "border: 1px solid #000; border-radius: 5px; background-color:#fff; font-size:14pt;";
```

Um `<div>` com `<input>` (ou `<textarea>` se multiline) e um `<button>` OK. DOM sempre renderiza **acima** do
canvas WebGL, então uma barra desenhada em uGUI ficaria atrás dela e duplicando a mesma função.

Usar sua arte na web exigiria esconder esse `<div>` mantendo o `<input>` focado — `opacity: 0` +
`pointer-events: none`, porque é ele que recebe a digitação — e tirar a altura do teclado de
`window.visualViewport`, já que `TouchScreenKeyboard.area` não existe na WebGL. Descartado: depende de
detectar por `MutationObserver` um elemento que o Unity cria sem `id` nem classe, com estilo inline que muda
entre versões.

## O que o pacote faz na web: consertar o teclado que não abre

O problema real na web não era a aparência da barra — era o teclado **não abrir** em alguns devices.

### A cadeia

```
navigator.appVersion
    ↓  /Mobile|Android|iP(ad|hone)/          UnityLoader.js:406
Module.SystemInfo.mobile
    ↓  JS_SystemInfo_IsMobile                SystemInfo.js:32
TouchScreenKeyboard.isSupported / isInPlaceEditingAllowed
    ↓  TouchScreenKeyboardShouldBeUsed()     TMP_InputField.cs:1538
JS_MobileKeyboard_Show → <div> HTML          MobileKeyboard.js
```

Na WebGL o gate do TMP não é `isSupported` direto:

```csharp
case RuntimePlatform.WebGLPlayer:
    return !TouchScreenKeyboard.isInPlaceEditingAllowed;
```

`isInPlaceEditingAllowed` não tem export JS próprio — é resolvido no módulo C++ do WebGL. Como o único sinal
de "mobile" que atravessa a ponte é o `IsMobile`, quase certamente deriva dele, mas isso não foi possível
provar lendo o binário.

### Onde o regex falha

- **iPad, iPadOS 13+** — Safari manda UA de macOS desktop. Sem `Mobile`, sem `iPad`, sem `iPhone`.
- **Tablet Android com "Site para computador"** — perde o token `Android`.
- WebViews de app com UA customizada.

O próprio Unity sabe que o regex é insuficiente: em `UnityLoader.js:909` ele usa
`navigator.maxTouchPoints > 1` pra detectar iPad — mas **só** na mensagem de erro do Safari, não no `mobile`.

### Não é o template

Hipótese testada e descartada. Existem dois testes de UA, em lugares diferentes:

| | Loader | Template |
|---|---|---|
| Arquivo | `UnityLoader.js:406` | `WebGLTemplates/Base/*/index.html:108` |
| Propriedade | `navigator.appVersion` | `navigator.userAgent` |
| Regex | `/Mobile\|Android\|iP(ad\|hone)/` | `/iPhone\|iPad\|iPod\|Android/i` |
| Controla | teclado nativo | viewport meta + classe CSS `unity-mobile` |

O `UnityLoader.js` é **gerado pelo build** a partir do módulo WebGL, não vem do template. Verificado idêntico,
byte a byte na mesma linha 406, em 6000.3.8, 6000.3.9, 6000.3.14, 6000.4.3 e 6000.4.6. O bloco do template é
idêntico em Default, Minimal e PWA.

Ou seja: template custom sem o bloco mobile dá canvas mal escalado, mas o teclado se comporta igual.

Diferenças que **de fato** explicam "funciona num projeto e noutro não", em ordem de probabilidade:

1. **Device/navegador**, não o projeto. É a explicação dominante.
2. **Versão do Unity** do projeto — o regex pode ter sido outro antes de 6000.x (não verificável com os
   installs disponíveis).
3. **Checkbox `Hide Mobile Input`** no `TMP_InputField`. Desde 2022.1 o `WebGLPlayer` entrou no switch de
   `shouldHideMobileInput` (`TMP_InputField.cs:413`), então ela passou a valer na web e muda o
   `InPlaceEditing()`.
4. Plugin de terceiro tipo `WebGLInput`, presente num projeto e não noutro.

### A correção

`Module.SystemInfo.mobile` é lido **na hora da chamada**, não cacheado do lado C#. Dá pra sobrescrever depois
do load. `Runtime/Plugins/WebGL/GGToolsWebGLKeyboard.jslib`:

```js
GGToolsWebGL_ForceMobileKeyboard: function()
{
    if (typeof Module === "undefined" || !Module.SystemInfo) return 0;
    if (Module.SystemInfo.mobile) return 1;
    if (typeof navigator === "undefined" || !(navigator.maxTouchPoints > 1)) return 0;

    Module.SystemInfo.mobile = true;
    return 2;
}
```

Chamado por `WebGLKeyboardBridge` num `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`, independente do
manager — o manager está desligado na web, então o bridge precisa rodar sozinho.

O gate `maxTouchPoints > 1` mantém desktop com mouse intocado. Opt-out pelo define
`GGTOOLS_NO_WEBGL_KEYBOARD_FIX`.

Por que via `.jslib` e não pelo template: dentro de um jslib o `Module` já está em escopo, e a correção viaja
junto com o pacote em vez de precisar ser recolada no `index.html` de cada projeto. Pelo template também
funciona:

```js
createUnityInstance(canvas, config, onProgress).then((unityInstance) => {
  if (navigator.maxTouchPoints > 1) unityInstance.Module.SystemInfo.mobile = true;
});
```

Não dá pra pré-setar via `config` do `createUnityInstance`: `UnityLoader.js:271` faz
`Module.SystemInfo = (function(){...})()`, atribuição direta que sobrescreve qualquer valor prévio.

### Verificar num device

No console do navegador do tablet:

```js
/Mobile|Android|iP(ad|hone)/.test(navigator.appVersion)   // false = é esse o problema
navigator.maxTouchPoints                                   // > 1 = o bridge conserta
```

Com `showDiagnostics` ligado, o dump ganha `web=` com o `WebGLMobileState`:
`Desktop` / `AlreadyMobile` / `TouchWithDesktopUserAgent` / `Unavailable`.

### Pendente

O módulo WebGL não está instalado nas versões 6000.0.x deste projeto — `ProjectVersion.txt` aponta
6000.0.39f1, e nem ela nem a 6000.0.43f1 têm `WebGLSupport` em `PlaybackEngines`. Só os installs 6000.3.x e
6000.4.x têm. Instalar o módulo para 6000.0 ou subir a versão do projeto antes de buildar pra web.
