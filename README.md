# LinkedIn Messenger (prototype)

Fenêtre Windows autonome qui affiche `linkedin.com/messaging/` dans WebView2, avec icône dans la zone de notification.

## Choix technique : C# WinForms + WebView2

| | Tauri + WebView2 | C# + WebView2 (retenu) |
|---|---|---|
| Outils à installer | Rust + MSVC Build Tools (plusieurs Go) | SDK .NET 8 |
| Icône zone de notification | plugin | `NotifyIcon` intégré |
| Intercepter les notifications Web | pas exposé par wry | `CoreWebView2.NotificationReceived` |
| Code | Rust + config + plugins | un fichier `Program.cs` |

WinForms plutôt que WPF : WPF n'a pas d'icône de zone de notification et passerait de toute façon par WinForms pour ça.

## Lancer

```bash
dotnet run -c Release
```

Exécutable : `bin\Release\net8.0-windows\LinkedInMessenger.exe`. Profil (cookies de session) : `%LOCALAPPDATA%\LinkedInMessenger` ; le supprimer = se déconnecter.

## Ce que fait le prototype

- Ouvre la messagerie ; `/` et `/feed` sont redirigés vers la messagerie.
- Liens externes → navigateur par défaut. Connexion Google/Apple/Microsoft gardée dans l'app.
- `X` cache la fenêtre dans la zone de notification ; « Quitter » dans le menu de l'icône.
- Notifications : 1) notifications Web de LinkedIn interceptées → bulle Windows, clic = conversation ; 2) filet : le compteur `(N)` du titre augmente → bulle « N non lus ».
- Pas de bulle si la fenêtre est au premier plan.
- Compteur dans le titre de la fenêtre et l'infobulle de l'icône.
- Taille/position mémorisées, démarrage avec Windows optionnel (`--tray`), instance unique.
- `compact.css` : éléments LinkedIn masqués, modifiable sans recompiler.

## Validation (à remplir après essai réel)

| Point | État | Notes |
|---|---|---|
| Compilation + lancement | OK | |
| Connexion LinkedIn | OK | |
| Session conservée après redémarrage de l'app | OK | |
| Taille de fenêtre mémorisée | OK | |
| Lire / envoyer un message | ? | |
| Pièce jointe (envoi / téléchargement) | ? | |
| Fenêtre étroite (~400 px) | OK | |
| `compact.css` : nav masquée sans casser la mise en page | OK | plein cadre, une seule barre de défilement |
| Voie 1 : LinkedIn émet-il des notifications Web ? | ? | |
| Voie 2 : `(N)` dans le titre = messages seulement ? | ? | peut inclure les notifications LinkedIn |
| Notification avec fenêtre cachée depuis > 5 min | ? | |

## Limites connues

- Aucune API : tout dépend de l'interface Web de LinkedIn.
- Raccourcis clavier non faits : WebView2 intercepte le clavier.
- Si un autre processus verrouille `Cookies` au démarrage (vu après un redémarrage de Windows), WebView2 garde ses cookies en mémoire : LinkedIn paraît déconnecté. L'app le détecte 15 s après le premier chargement et se relance une fois (`--retry`).
