# Copiador de XML iCompany

Aplicacao Windows para copiar ou mover XMLs do servidor para uma pasta local, com configuracao salva em JSON e log ao vivo.

## Uso

1. Abra o aplicativo `XmlCopiador.exe`.
2. Confira a pasta de destino, mes, ano e acao:
   - `MOVE`: move os XMLs, removendo da origem.
   - `COPY`: copia os XMLs, mantendo na origem.
3. Edite a grade de empresas quando precisar trocar cliente, CNPJ, UF ou pasta de origem.
4. Clique em `Salvar configuracao`.
5. Clique em `Iniciar`.

## Configuracao

O arquivo `XmlCopiador.config.json` fica ao lado do executavel. Ele guarda:

- pasta base de destino;
- acao padrao (`MOVE` ou `COPY`);
- nomes das pastas dos meses;
- empresas, CNPJs, UF e origem dos XMLs;
- opcao de gerar XML via PostgreSQL, periodo e conexoes de banco.

Se esse arquivo existir, o aplicativo abre obedecendo exatamente essa configuracao.

Se esse arquivo nao existir, o aplicativo abre sem empresas configuradas e nao cria o JSON automaticamente. O arquivo novo so sera criado quando voce preencher os dados e clicar em `Salvar configuracao`.

## Atualizacao automatica

O app pode verificar uma atualizacao ao iniciar. Para ativar, edite o `XmlCopiador.config.json` e configure:

```json
"Update": {
  "Enabled": true,
  "Manifest": "https://raw.githubusercontent.com/cdlawliet/xmlCopiador/main/XmlCopiador.update.json"
}
```

O manifesto de update deve ter este formato:

```json
{
  "Version": "2.0.0",
  "DownloadUrl": "https://raw.githubusercontent.com/cdlawliet/xmlCopiador/v2.0.0/release/XmlCopiador.exe",
  "Sha256": "8A50ABABF84D2F1F0668C787BB872A3BBE0F5DEF9314C50A3DA4F05DCB1F126F"
}
```

O exemplo acima usa o proprio GitHub publico como ponto de download, por meio do arquivo `release/XmlCopiador.exe` versionado pela tag.

Se `Sha256` for preenchido, o app so atualiza quando o hash do arquivo baixado confere. Se ficar vazio, ele apenas baixa e substitui.

## Mascara usada

Para cada empresa, o aplicativo procura arquivos com este formato:

`UF + ano(2 digitos) + mes(2 digitos) + CNPJ + modelo + * + sufixo`

Exemplo para NFe autorizada de maio/2026:

`2226050000000000000055*-nfe.xml`

Os modelos usados sao:

- `55`: NFe
- `65`: NFCe

Os sufixos usados sao:

- `-nfe.xml`: autorizadas
- `-can.xml`: canceladas

## Geracao via PostgreSQL

Quando `Gerar XML` estiver marcado, o app conecta em cada banco ativo, cria a funcao `exportar_xml_nfe(date, date, text)` se ela ainda nao existir, executa a funcao para o periodo escolhido e so depois inicia a rotina normal de copiar ou mover XMLs.
