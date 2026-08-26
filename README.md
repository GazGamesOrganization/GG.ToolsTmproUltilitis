# GGTools — TMProUltilitis

Utilitários de texto para `TextMeshProUGUI` + **Mobile Input Preview**.

- Assembly runtime: `GGTools.TmproUltilitis`
- Namespace: `GGTools.TMProUltilitis`
- Unity: 6000.0+ (TMP vem dentro de `com.unity.ugui` 2.0.0)

Detalhes internos do cálculo de altura do teclado e da instrumentação de debug em device:
[MOBILE_INPUT_PREVIEW_INTERNALS.md](MOBILE_INPUT_PREVIEW_INTERNALS.md).

---

## 1. TMProUltils (typewriter e rich text)

Classe estática de extensões em `Runtime/TmproUltilitis.cs`.

```csharp
using GGTools.TMProUltilitis;

myText.Type(this, "Olá mundo", 0.03f);   // datilografa char a char
myText.IsTyping();                        // está digitando?
myText.StopType(this);                    // interrompe

texto.AutoPage(120);                      // insere <page> respeitando palavras
texto.Colorize(Color.red).Bold().Italic();
```

---

## 2. Mobile Input Preview

Caixa de texto que aparece **acima do teclado nativo** quando um `TMP_InputField` é selecionado,
espelhando em tempo real o que está sendo digitado. Resolve o caso em que o teclado do celular cobre
o campo e o jogador digita às cegas.

**A arte é sua.** O manager não cria nem estiliza nada — ele só decide *quando* mostrar, *em que altura*
posicionar e *qual texto* escrever. Sprites, cores, fontes, layout e animação ficam no seu prefab.

### Montagem

```
MobileInputPreviewManager          <- MobileInputPreview  (o manager)
└── Canvas                         <- MobileInputPreviewView  (sua arte, filho do manager)
    ├── BG                             backdrop de tela cheia (opcional)
    └── Bar                            RectTransform ancorado embaixo
        ├── Preview  (TMP_Text)
        └── Done     (Button)
```

1. GameObject vazio na cena de boot → componente **GGTools > TMPro Ultilitis > Mobile Input Preview**.
2. Monte sua UI como **filha** dele. O `Canvas` pode ser Screen Space - Overlay ou Camera, tanto faz.
3. No root da arte, adicione **GGTools > TMPro Ultilitis > Mobile Input Preview View** e ligue as referências.
4. O manager acha a view sozinho via `GetComponentInChildren` — só preencha o campo `view` se tiver mais de uma.

#### Referências da View

| Campo | Obrigatório | O que é |
|---|---|---|
| `barRect` | **sim** | A barra **dentro** do canvas — nunca a raiz do Canvas. Só `anchoredPosition.y` é escrito; x, tamanho e âncoras preservados. **Não importa** pivot, âncora, stretch ou layout group: o manager mede onde a barra está na tela e corrige pela diferença, alinhando a borda de baixo do conteúdo com o topo do teclado. |

> **Erro mais comum:** apontar `barRect` pra raiz do Canvas. Aí o manager move o canvas inteiro, e como a raiz do Canvas tem pivot 0.5 e altura de tela cheia, a barra aparece **cortada exatamente no meio**. O componente detecta e grita: um `Debug.LogWarning` e um `>>> ... <<<` no topo do dump de diagnóstico.
| `previewLabel` | **sim** | `TMP_Text` que recebe o texto espelhado + caret. |
| `alignWholeContent` | — | Default `true`. Alinha a borda de baixo de **tudo** dentro do `barRect`, filhos inclusos — um botão centrado na borda pendura metade pra fora e ficaria embaixo do teclado. **Desligue** se o `barRect` contiver um backdrop de tela cheia, senão os bounds pegam a tela toda e a barra sobe demais. |
| `canvas` | não | Canvas da view. Usado pra converter pixels em unidades de referência e pra desligar o overlay inteiro. Auto-preenchido do pai. |
| `background` | não | Backdrop de tela cheia. Liga/desliga junto com a barra. |
| `doneButton` | não | Botão de confirmar. Fecha o teclado. |
| `backgroundButton` | não | `Button` no backdrop. Tocar fora também fecha o teclado. |
| `diagnosticsLabel` | não | `TMP_Text` que recebe o dump de valores medidos quando `showDiagnostics` está ligado. |

Faltando `barRect` ou `previewLabel`, o manager emite **um** `Debug.LogWarning` e fica inerte.

O `Reset()` da view já tenta preencher `canvas`, `barRect`, `previewLabel` e `doneButton` ao adicionar o componente.

### Auto-spawn (opcional)

Se existir um prefab chamado **`GGToolsMobileInputPreview`** em qualquer pasta `Resources/`, ele é
instanciado sozinho após a primeira cena carregar — aí não precisa colocar nada em cena nenhuma.
Sem esse prefab, o manager tem que ser autorado numa cena.

`dontDestroyOnLoad` (default `true`) mantém manager + arte vivos entre cenas.

### Settings do manager

Só comportamento. Nada visual.

| Grupo | Campo | Default | O que faz |
|---|---|---|---|
| Activation | `activationMode` | `Auto` | `Auto` = device com teclado nativo, ou Editor com simulação ligada. `Always` = qualquer plataforma. `Never` = desliga. |
| Activation | `simulateInEditor` | `true` | No Editor, finge um teclado pra testar o layout sem build. |
| Keyboard height | `keyboardHeightSource` | `Auto` | Como medir. Só mexa pra debugar — ver seção abaixo. |
| Keyboard height | `simulatedKeyboardHeightPercent` | `0.45` | Altura do teclado falso no Editor, em fração da tela. |
| Keyboard height | `fallbackKeyboardHeightPercent` | `0.45` | Último recurso, quando nenhuma medição está disponível. |
| Keyboard height | `extraKeyboardMargin` | `0` | Offset extra acima do teclado, em px reais. Negativo sobrepõe. |
| Keyboard height | `respectSafeArea` | `true` | Impede que a barra caia dentro de notch / gesture bar. |
| Debug | `showDiagnostics` | `false` | Escreve todos os valores medidos no `diagnosticsLabel` da view. |

### Medição da altura do teclado

`TouchScreenKeyboard.area` **não é confiável** — vários Androids, tablets em especial, devolvem rect zerado
ou valor defasado. Por isso o `Auto` prefere, no Android, perguntar direto pro framework:

```
decorView.getWindowVisibleDisplayFrame(rect)
keyboardPx = decorView.getHeight() - rect.bottom - baseline
```

O `baseline` é o inset de baixo medido enquanto **nenhum campo** está focado — ou seja, a barra de navegação.
Subtrair ele evita confundir nav bar com teclado. É recapturado a cada 0.5s enquanto a view está escondida.

| `keyboardHeightSource` | Quando usar |
|---|---|
| `Auto` | Padrão. Editor → simulado; Android → window frame; resto → `TouchScreenKeyboard.area`. |
| `AndroidVisibleFrame` | Forçar o JNI. Cai pro `area` se a ponte falhar. |
| `TouchScreenKeyboardArea` | Comparar com o comportamento antigo. |
| `FixedPercent` | Ignora tudo e usa `fallbackKeyboardHeightPercent`. |
| `EditorSimulated` | Força o teclado falso mesmo em build. |

Ligue `showDiagnostics`. Não precisa configurar mais nada — sem um `TMP_Text` no campo `diagnosticsLabel`,
o dump é desenhado via IMGUI numa faixa preta no topo da tela, em qualquer build. Se você **ligar** um
`diagnosticsLabel`, ele usa o label e desliga o IMGUI. Em ambos os casos também vai pro console
(`adb logcat -s Unity`), 1x por segundo.

```
src=AndroidVisibleFrame kb=612 raw=612 base=0 area=0 off=612 scr=2400x1080 safe=0 sf=1.33 field=1 act=1 view=1 tsk=1
```

| Campo | Significado |
|---|---|
| `src` | Fonte que venceu. `FixedPercent` no device = nenhuma medição funcionou. |
| `kb` | Altura do teclado em px, já sem o baseline. |
| `raw` | Inset bruto do window frame, antes de tirar o baseline. |
| `base` | Baseline (nav bar) medido. `-1` = nunca capturado. |
| `area` | O que o `TouchScreenKeyboard.area` diz, pra comparação. |
| `off` | Offset final aplicado na barra. |
| `scr` | `Screen.width x Screen.height`. |
| `safe` | `Screen.safeArea.yMin`. |
| `sf` | `canvas.scaleFactor`. |
| `field` | 1 = tem `TMP_InputField` focado. |
| `act` | 1 = `activationMode` permitiu. |
| `view` | 1 = view achada e com `barRect` + `previewLabel` ligados. |
| `tsk` | `TouchScreenKeyboard.isSupported`. |
| `bot` | Onde a borda de baixo da barra **estava** antes da correção, em px de tela. |
| `corr` | Correção aplicada, em px. Estabiliza perto de `0` quando o teclado para de animar. `corr` grande e constante = algo está brigando pela `anchoredPosition` (Animator, LayoutGroup). |

O dump aparece **mesmo quando a feature está desligada ou mal configurada** — é justamente aí que ele serve.
| Text | `caretBlinkRate` | `0.53` | Segundos por piscada. `<= 0` deixa o caret fixo. |
| Text | `caretGlyph` | `"\|"` | Glifo do caret. Vazio = sem caret. |
| Text | `charWindow` | `60` | Máximo de chars visíveis. Texto maior é janelado em volta do caret, com `…` nas pontas. |

### Desligar em um campo específico

Adicione **GGTools > TMPro Ultilitis > Mobile Input Preview Target** no `TMP_InputField` e desmarque
`enablePreview`. Sem esse componente o padrão é ligado — zero setup por campo.

### Comportamento

- **Password** (`Content Type = Password`) — mostra `asteriskChar`, nunca o texto puro.
- **Texto longo** — janela de `charWindow` chars centrada no caret, com `…` indicando corte.
- **Rich text** — deixe `Rich Text` desmarcado no `previewLabel`, senão tags digitadas pelo jogador renderizam.
- **Done / tocar no BG** — chama `DeactivateInputField()` e limpa a seleção do `EventSystem`, fechando o teclado.
- Submit, clicar fora, desabilitar o campo ou destruí-lo escondem a view.
- Requer um `EventSystem` na cena (obrigatório pra qualquer UI clicável). Sem ele, um único warning.

### WebGL

Na WebGL o `MobileInputPreview` **se desliga sozinho** em modo `Auto`. Motivo: o Unity já desenha a própria
barra de input em HTML (`<div style="position:fixed; bottom:0">` com `<input>` e botão OK) por cima do
canvas. DOM sempre renderiza acima do canvas WebGL, então a sua arte ficaria escondida atrás e duplicada.

O que o pacote faz na web é outra coisa: **consertar o teclado que não abre**.

`WebGLKeyboardBridge` + `Runtime/Plugins/WebGL/GGToolsWebGLKeyboard.jslib`.

O Unity marca o navegador como mobile em `UnityLoader.js` com:

```js
mobile: /Mobile|Android|iP(ad|hone)/.test(navigator.appVersion)
```

Esse regex falha em devices com touch de verdade que mandam UA de desktop:

- **iPad, iPadOS 13+** — Safari manda UA de macOS por padrão
- **Tablet Android com "Site para computador"** — perde o token `Android`
- WebViews de app com UA customizada

Nesses casos `TouchScreenKeyboard.isSupported` é `false`, o `TMP_InputField` nunca abre teclado nenhum, e o
campo fica inutilizável. Foi isso que fez uns jogos seus funcionarem na web e outros não — é **device**, não
template nem projeto.

O bridge sobrescreve `Module.SystemInfo.mobile` quando `navigator.maxTouchPoints > 1` — mesmo heurístico que
o próprio Unity usa pra detectar iPad. Desktop com mouse fica intocado. Roda sozinho em
`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`.

```csharp
// Opt-out: defina GGTOOLS_NO_WEBGL_KEYBOARD_FIX nos Scripting Define Symbols
// Manual:
WebGLKeyboardBridge.ForceMobileKeyboard();
WebGLKeyboardBridge.PeekState();   // só lê, não altera
```

`WebGLMobileState`: `Desktop` / `AlreadyMobile` / `TouchWithDesktopUserAgent` / `Unavailable`.
Quando força, loga no console. Aparece como `web=` no dump de diagnóstico.

Teste de 5 segundos no console do navegador do device que falha:

```js
/Mobile|Android|iP(ad|hone)/.test(navigator.appVersion)   // false = é esse o problema
navigator.maxTouchPoints                                   // > 1 = o bridge conserta
```

Pra usar sua arte na web em vez da barra do Unity, seria preciso esconder o `<div>` que o Unity cria
mantendo o `<input>` focado, e tirar a altura do teclado de `window.visualViewport`. Não implementado —
depende de detectar um elemento que o Unity cria sem `id`, o que quebra a cada versão.

### Testar no Editor

1. Cena com `EventSystem`, um `Input Field - TextMeshPro` e o manager + sua arte.
2. Play, clicar no campo.
3. A barra sobe pra ~45% da altura da tela, simulando o teclado.

---

## Arquivos

```
Runtime/
  TmproUltilitis.cs                        TMProUltils (typewriter + rich text)
  MobileInputPreview/
    MobileInputPreview.cs                  manager: watcher do EventSystem, altura, texto
    KeyboardHeightSensor.cs                medição da altura do teclado (JNI no Android, area no resto)
    MobileInputPreviewView.cs              contrato da arte autorada (barRect/label/bg/botões)
    MobileInputPreviewSettings.cs          configuração de comportamento
    MobileInputPreviewTarget.cs            opt-out por campo
    WebGLKeyboardBridge.cs                 conserta o teclado em iPad/tablet com UA de desktop
  Plugins/WebGL/
    GGToolsWebGLKeyboard.jslib             sobrescreve Module.SystemInfo.mobile
```
