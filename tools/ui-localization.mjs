import {readFileSync} from 'node:fs';
import {transformSync,types as t} from '@babel/core';
import {uncoveredUiMessages} from './ui-message-audit.mjs';

export const normalizeText=value=>value.replace(/\s+/g,' ').trim();
export function localizationResources(){
  const source=JSON.parse(readFileSync(new URL('../web/src/i18n/source-messages.json',import.meta.url),'utf8'));
  const translated=JSON.parse(readFileSync(new URL('../web/src/i18n/en.json',import.meta.url),'utf8'));
  const extra=JSON.parse(readFileSync(new URL('../web/src/i18n/extra.json',import.meta.url),'utf8'));
  const english={};for(const [id,value] of Object.entries(translated)){if(!source[Number(id)])throw new Error('Unknown translation id: '+id);english[source[Number(id)].text]=value;}
  Object.assign(english,extra); // Corrections/extensions override the initial catalog, not vice versa.
  const turkish=Object.fromEntries(Object.keys(english).map(key=>[key,key]));
  // Own messages already rendered in English remain translatable after switching to Turkish.
  for(const [key,value] of Object.entries({...english})){if(!Object.hasOwn(english,value))english[value]=value;if(!Object.hasOwn(turkish,value))turkish[value]=key;}
  return{english,turkish};
}
function jsxText(value){
  const lines=value.split(/\r\n|\n|\r/);let last=0;for(let i=0;i<lines.length;i++)if(/[^ \t]/.test(lines[i]))last=i;
  return lines.map((line,i)=>{let text=line.replace(/\t/g,' ');if(i!==0)text=text.replace(/^ +/,'');if(i!==lines.length-1)text=text.replace(/ +$/,'');return text+(text&&i!==last?' ':'');}).join('');
}
export function transformUi(code,filename,english=localizationResources().english){
  const translated=value=>Object.hasOwn(english,normalizeText(value))&&english[normalizeText(value)]!==normalizeText(value);
  let used=false;let hooks=false;let locale=false;let runtime=false;
  const tr=(text,values=[])=>{used=true;return t.callExpression(t.identifier('__homeText'),[t.stringLiteral(text),...(values.length?[t.objectExpression(values.map((value,index)=>t.objectProperty(t.identifier('v'+index),value)))]:[])]);};
  const result=transformSync(code,{filename,babelrc:false,configFile:false,sourceMaps:true,parserOpts:{plugins:['typescript',...(filename.endsWith('.tsx')?['jsx']:[])]},plugins:[()=>({visitor:{
    Function:{exit(path){
      const name=path.node.id?.name??(path.parentPath.isVariableDeclarator()?path.parent.id.name:null);
      if(!name||(!/^[A-Z]/.test(name)&&!/^use[A-Z]/.test(name))||path.node.async||path.isObjectMethod()||path.isClassMethod())return;
      let jsx=false;path.traverse({JSXElement(child){jsx=true;child.stop();},JSXFragment(child){jsx=true;child.stop();}});
      if(!jsx&&!/^use[A-Z]/.test(name))return;
      if(!t.isBlockStatement(path.node.body))path.node.body=t.blockStatement([t.returnStatement(path.node.body)]);
      path.node.body.body.unshift(t.expressionStatement(t.callExpression(t.identifier('__homeUseLanguage'),[])));hooks=true;
    }},
    JSXText(path){const value=jsxText(path.node.value);if(translated(value)){path.replaceWith(t.jsxExpressionContainer(tr(value)));path.skip();}},
    JSXExpressionContainer:{exit(path){
      const parent=path.parentPath;
      const className=parent.isJSXElement()?parent.node.openingElement.attributes.find(attribute=>t.isJSXAttribute(attribute)&&attribute.name.name==='className'):null;
      const error=className&&t.isStringLiteral(className.value)&&className.value.value.split(' ').includes('form-error');
      const expression=path.node.expression;
      if(error&&!t.isJSXEmptyExpression(expression)&&!t.isJSXElement(expression)&&!t.isJSXFragment(expression)){
        path.node.expression=t.callExpression(t.identifier('__homeRuntimeText'),[expression]);runtime=true;
      }
    }},
    'MemberExpression|OptionalMemberExpression'(path){
      if(!t.isIdentifier(path.node.property)||!['message','lastError'].includes(path.node.property.name)||path.node.computed)return;
      let parent=path.parentPath;
      while(parent&&!parent.isJSXExpressionContainer()){
        if(!parent.isLogicalExpression()&&!parent.isConditionalExpression()&&!parent.isTemplateLiteral()&&!parent.isBinaryExpression())return;
        if(parent.isConditionalExpression()&&parent.node.test===path.node)return;
        parent=parent.parentPath;
      }
      if(!parent||parent.parentPath.isJSXAttribute()&&!['title','aria-label'].includes(parent.parent.name.name))return;
      path.replaceWith(t.callExpression(t.identifier('__homeRuntimeText'),[path.node]));path.skip();runtime=true;
    },
    StringLiteral(path){
      if(!translated(path.node.value))return;
      if(path.parentPath.isImportDeclaration()||path.parentPath.isExportNamedDeclaration()||path.parentPath.isExportAllDeclaration())return;
      if(path.findParent(parent=>parent.type.startsWith('TS')&&!['TSAsExpression','TSSatisfiesExpression','TSNonNullExpression'].includes(parent.type)))return;
      if(path.parentPath.isObjectProperty()&&path.key==='key'||path.parentPath.isMemberExpression()&&path.key==='property')return;
      if(path.parentPath.isCallExpression()&&t.isIdentifier(path.parent.callee)&&['__homeText','translate','runtimeText','t'].includes(path.parent.callee.name))return;
      if(path.parentPath.isJSXAttribute()){
        if(!['title','label','placeholder','aria-label','alt','description'].includes(path.parent.name.name))return;
        path.replaceWith(t.jsxExpressionContainer(tr(path.node.value)));path.skip();return;
      }
      if(!path.getFunctionParent()&&path.parentPath.isObjectProperty()&&path.key==='value'){
        const property=path.parentPath;property.replaceWith(t.objectMethod('get',property.node.key,[],t.blockStatement([t.returnStatement(tr(path.node.value))]),property.node.computed));property.skip();return;
      }
      if(!path.getFunctionParent()&&path.parentPath.isArrayExpression())return;
      path.replaceWith(tr(path.node.value));path.skip();
    },
    ArrayExpression:{exit(path){
      if(path.getFunctionParent())return;const entries=path.node.elements.map((node,index)=>({node,index})).filter(({node})=>t.isStringLiteral(node)&&translated(node.value));if(!entries.length)return;
      let result=path.node;for(const{node,index}of entries)result=t.callExpression(t.memberExpression(t.identifier('Object'),t.identifier('defineProperty')),[result,t.numericLiteral(index),t.objectExpression([t.objectProperty(t.identifier('get'),t.arrowFunctionExpression([],tr(node.value))),t.objectProperty(t.identifier('enumerable'),t.booleanLiteral(true))])]);
      path.replaceWith(result);path.skip();
    }},
    TemplateLiteral(path){
      if(path.parentPath.isTaggedTemplateExpression())return;
      const text=path.node.quasis.map((part,index)=>part.value.cooked+(index<path.node.expressions.length?`{{v${index}}}`:'')).join('');if(!translated(text))return;
      path.replaceWith(tr(text,path.node.expressions));path.skip();
    },
    CallExpression(path){
      const callee=path.node.callee;if(!t.isMemberExpression(callee)||!t.isIdentifier(callee.property))return;
      if(['toLocaleString','toLocaleDateString','toLocaleTimeString','toLocaleLowerCase','toLocaleUpperCase'].includes(callee.property.name)&&t.isStringLiteral(path.node.arguments[0])&&['tr-TR','tr'].includes(path.node.arguments[0].value)){path.node.arguments[0]=t.callExpression(t.identifier('__homeLocale'),[]);locale=true;}
    },
    NewExpression(path){const callee=path.node.callee;if(t.isMemberExpression(callee)&&t.isIdentifier(callee.object,{name:'Intl'})&&['DateTimeFormat','NumberFormat','RelativeTimeFormat'].includes(callee.property.name)&&t.isStringLiteral(path.node.arguments[0])&&path.node.arguments[0].value==='tr-TR'){path.node.arguments[0]=t.callExpression(t.identifier('__homeLocale'),[]);locale=true;}},
    Program:{exit(path){
      const imports=[];if(used)imports.push(t.importSpecifier(t.identifier('__homeText'),t.identifier('translate')));if(hooks)imports.push(t.importSpecifier(t.identifier('__homeUseLanguage'),t.identifier('useUiLanguage')));if(locale)imports.push(t.importSpecifier(t.identifier('__homeLocale'),t.identifier('uiLocale')));if(runtime)imports.push(t.importSpecifier(t.identifier('__homeRuntimeText'),t.identifier('runtimeText')));
      if(imports.length)path.unshiftContainer('body',t.importDeclaration(imports,t.stringLiteral('/src/i18n/index.ts')));
    }}
  }})]});
  return {code:result.code,map:result.map};
}
export function uiLocalization(){
  let dictionaries=localizationResources();const neutral=JSON.parse(readFileSync(new URL('../web/src/i18n/neutral-messages.json',import.meta.url),'utf8'));
  return{name:'home-ui-localization',enforce:'pre',resolveId(id){if(id==='virtual:home-translations')return'\0home-translations';},load(id){if(id==='\0home-translations'){dictionaries=localizationResources();return`export const english=${JSON.stringify(dictionaries.english)};export const turkish=${JSON.stringify(dictionaries.turkish)};`;}},transform(code,id){const file=id.split('?')[0];if(!/\/src\/.*\.tsx?$/.test(file.replaceAll('\\','/'))||file.includes('/i18n/')||file.endsWith('.d.ts'))return;const missing=uncoveredUiMessages(code,file,dictionaries.english,neutral);if(missing.length)throw new Error(`Missing UI translations in ${file}: ${missing.map(item=>`${item.line}: ${item.key}`).join('; ')}`);return transformUi(code,file,dictionaries.english);}};
}
