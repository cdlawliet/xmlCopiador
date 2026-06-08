# XmlCopiador

Aplicativo Windows para copiar ou mover XMLs do iCompany para uma pasta local, separando por empresa, mes, NFe/NFCe e autorizadas/canceladas.

## Recursos

- configuracao salva em `XmlCopiador.config.json`;
- grade de empresas com CNPJ, estado e origem dos XMLs;
- acao `COPY` ou `MOVE`;
- criacao automatica das pastas apenas quando existem XMLs para aquele grupo;
- log em tempo real;
- barra de progresso interna e indicador no icone;
- auto-update opcional via manifesto publico.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\gerar_exe_xml.ps1
```

O executavel sera publicado em:

```text
dist\XmlCopiador\XmlCopiador.exe
```

## Auto-update

Configure o `XmlCopiador.config.json` ao lado do executavel:

```json
{
  "Update": {
    "Enabled": true,
    "Manifest": "https://raw.githubusercontent.com/cdlawliet/xmlCopiador/main/XmlCopiador.update.json"
  }
}
```

O manifesto publico deste repositorio e:

```text
https://raw.githubusercontent.com/cdlawliet/xmlCopiador/main/XmlCopiador.update.json
```

O `DownloadUrl` do manifesto aponta para o executavel publicado em GitHub Releases.
