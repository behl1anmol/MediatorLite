---
title: Releases
nav_order: 9
has_children: true
---

# Releases

Release notes for each published MediatorLite version. Pick a version from the
navigation on the left, or the list below.

{% assign release_pages = site.pages | where_exp: "p", "p.parent == 'Releases'" | sort: "title" %}
<ul>
{% for p in release_pages %}
  <li><a href="{{ p.url | relative_url }}">{{ p.title }}</a></li>
{% endfor %}
</ul>

---

## Adding a new release note

1. Create `docs/releases/<version>.md`.
2. Begin the file with this front matter, then write the notes below it:

   ```yaml
   ---
   title: "vX.Y.Z"
   parent: Releases
   ---
   ```

The page auto-joins the **Releases** section in the left navigation and the list
above — no other file needs editing.
