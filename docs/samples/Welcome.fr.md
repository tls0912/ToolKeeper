# Bienvenue dans 汗青

Ouvrez un fichier Markdown, lisez confortablement et appuyez sur **Ctrl+E** lorsque vous souhaitez le modifier.

En mode aperçu, la partie gauche affiche les titres de niveau H1 à H6. Cliquez sur un titre pour accéder à sa section ; le défilement met également en évidence la section en cours. En mode édition, le texte source se trouve à gauche et l’aperçu en temps réel à droite, tandis que la liste des titres est masquée. Faites glisser la séparation pour ajuster la largeur des colonnes.

> Vos documents restent sur votre ordinateur. Hanqing ne nécessite aucun compte.

## Premiers pas

| Action | Raccourci |
| --- | --- |
| Ouvrir un document | Ctrl+O |
| Nouveau document | Ctrl+N |
| Basculer entre aperçu et édition | Ctrl+E |
| Enregistrer | Ctrl+S |
| Rechercher du texte | Ctrl+F |
| Résultat de recherche suivant | F3 |
| Fermer l’onglet actuel | Ctrl+W |
| Lire en plein écran | F11 |

La barre d’outils à gauche permet de créer des documents ou des fenêtres, d’enregistrer une copie et d’ouvrir des documents récents. Elle propose également des commandes de police et de thème ; la langue et la taille de la fenêtre se trouvent dans Plus. Cliquez sur le bouton en haut de la barre d’outils pour développer les noms, descriptions et raccourcis, puis cliquez à nouveau pour les masquer. Passer le pointeur sur la barre d’outils ne change pas son état.

Le réglage Plan des chapitres, dans Plus, permet de sélectionner les niveaux H1 à H6 pour l’aperçu Markdown et l’exportation PDF. L’option permettant de rouvrir les fichiers ouverts au prochain démarrage est désactivée par défaut. Activez-la pour retrouver ces documents au lancement suivant ; les modifications non enregistrées continuent à déclencher une demande de confirmation.

Les réglages de police sont indépendants pour l’interface, l’aperçu de lecture et l’éditeur. Chacun possède sa propre police et sa propre taille ; les changements s’appliquent immédiatement et sont mémorisés.

En mode édition, une barre de mise en forme apparaît au-dessus du document. Choisissez une police pour l’éditeur — automatique à chasse fixe, écriture traditionnelle ou police installée —, saisissez une taille de 8 à 72 ou utilisez **− / +**. Ces préférences modifient l’affichage de l’éditeur et sont mémorisées sans être inscrites dans le fichier Markdown. La barre d’outils est masquée en mode aperçu ou lorsque tous les documents sont fermés.

Les nouvelles installations utilisent le thème Bambou (clair), qui associe une interface en bambou à un fond de papier. Les deux thèmes Bambou présentent des fibres de papier visibles. Toute préférence de thème existante est conservée. Choisissez un thème dans Plus → Général → Thème ; le bouton de la barre latérale fait défiler les thèmes Clair, Sombre, Bambou (clair) et Bambou (sombre).

Dans les thèmes Bambou, les polices automatiques de l’interface et de l’aperçu utilisent l’écriture traditionnelle. Pour le chinois traditionnel, une police BiauKai installée est privilégiée. Vous pouvez aussi sélectionner directement l’écriture traditionnelle. Les choix explicites de police sont conservés, et l’éditeur utilise par défaut une police à chasse fixe.

La taille du texte par défaut est de 15 pour l’interface et de 16 pour l’aperçu et l’éditeur. La barre d’outils est développée au départ. L’enregistrement automatique est activé par défaut : un document dont l’emplacement est défini est enregistré trois secondes après l’arrêt de la saisie. Pour un nouveau document, utilisez d’abord **Ctrl+S** afin de choisir son premier emplacement d’enregistrement.

## Exporter en PDF

Cliquez sur Exporter en PDF, sous Enregistrer dans la barre d’outils à gauche. Hanqing enregistre d’abord le fichier Markdown, puis crée un PDF dans le même dossier avec un horodatage local, par exemple `Notes.md` → `Notes_20260922_153012.pdf` (`yyyyMMdd_HHmmss`).

Pour un nouveau document, vous devez d’abord choisir l’emplacement du fichier Markdown. L’annulation ou l’échec de l’enregistrement interrompt l’exportation. Une confirmation vous est demandée avant de remplacer un PDF existant. Le PDF utilise des pages A4 blanches, la police et la taille actuelles de l’aperçu, et comprend l’intégralité du contenu ainsi que les images locales.

## Listes de tâches

Voici un exemple de liste de tâches Markdown. Lorsqu’aucun document n’est ouvert, cette présentation est en lecture seule. Ouvrez ou créez d’abord un document, puis essayez de cocher et d’enregistrer des éléments dans votre propre document.

- [x] Ouvrir un document Markdown
- [ ] Cocher un élément dans votre document et observer l’indicateur de modifications non enregistrées de l’onglet
- [ ] Appuyer sur Ctrl+S pour enregistrer les modifications

## Code

```csharp
var message = "Read. Edit. Save.";
Console.WriteLine(message);
```

Utilisez le bouton en haut à droite d’un bloc de code pour le copier. Vous pouvez également sélectionner du texte ordinaire et appuyer sur **Ctrl+Shift+C** pour copier le Markdown.

## Conseils d’édition

1. Utilisez le menu de paragraphe de la barre de mise en forme pour choisir le corps du texte ou des titres **H1 à H6**.
2. Sélectionnez du texte pour appliquer le gras, l’italique, le texte barré, le code en ligne ou un lien. **Ctrl+B** et **Ctrl+I** appliquent également le gras et l’italique. Ces actions insèrent du Markdown standard et peuvent être annulées avec **Ctrl+Z**.
3. Collez une URL sur le texte sélectionné pour créer un lien Markdown.
4. Collez une image en mode édition pour l’enregistrer dans un dossier `images` à côté du document.
5. Faites un clic droit dans l’aperçu et choisissez Modifier ici pour accéder au paragraphe correspondant.

Les documents en lecture seule restent protégés contre les modifications effectuées avec les boutons de mise en forme.

En mode édition, développez le panneau de recherche pour remplacer du texte. **Tout remplacer demande d’abord une confirmation.**

---

[Retour en haut](#bienvenue-dans-汗青)
