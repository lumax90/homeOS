import {parseSync,traverse,types as t} from '@babel/core';

const visibleAttributes=new Set(['title','label','placeholder','aria-label','alt','description']);
const sinks=new Set(['t','translate','__homeText','notify','setNotice','setError','setMessage','setToast']);
export const uiMessageKey=value=>value.replace(/\s+/g,' ').trim();
function visibleExpression(path){
  let current=path;
  while(current.parentPath){
    const parent=current.parentPath;
    if(parent.isJSXExpressionContainer())return !parent.parentPath.isJSXAttribute()||visibleAttributes.has(parent.parent.name.name);
    if(parent.isConditionalExpression()){if(parent.node.test===current.node)return false;current=parent;continue;}
    if(parent.isLogicalExpression()||parent.isBinaryExpression()&&parent.node.operator==='+'){current=parent;continue;}
    if(parent.isTSAsExpression()||parent.isTSNonNullExpression()){current=parent;continue;}
    return false;
  }
  return false;
}
function ownedString(path){
  if(path.parentPath.isJSXAttribute())return visibleAttributes.has(path.parent.name.name);
  if(path.parentPath.isObjectProperty()&&path.key==='value'){
    const key=path.parent.key.name??path.parent.key.value;
    if(['label','title','description','placeholder','hint'].includes(key))return true;
    const declaration=path.findParent(parent=>parent.isVariableDeclarator());
    if(declaration&&/(?:Labels|Names|labels|names|topics|states)$/.test(declaration.node.id?.name??''))return true;
  }
  let current=path;
  while(current.parentPath?.isConditionalExpression()||current.parentPath?.isLogicalExpression()){
    if(current.parentPath.isConditionalExpression()&&current.parentPath.node.test===current.node)return false;
    current=current.parentPath;
  }
  const call=current.parentPath;
  if(call?.isCallExpression()&&call.node.arguments[0]===current.node&&t.isIdentifier(call.node.callee)&&sinks.has(call.node.callee.name))return true;
  if(call?.isNewExpression()&&call.node.arguments[0]===current.node&&t.isIdentifier(call.node.callee,{name:'Error'}))return true;
  return visibleExpression(path);
}
/** Collect semantic UI message sites, not "text containing Turkish characters". */
export function collectUiMessages(code,filename){
  const ast=parseSync(code,{filename,babelrc:false,configFile:false,parserOpts:{plugins:['typescript',...(filename.endsWith('.tsx')?['jsx']:[])]}});
  const messages=new Map();const add=(value,line)=>{const key=uiMessageKey(value);if(key)messages.set(key,{key,line});};
  traverse(ast,{
    JSXText(path){add(path.node.value,path.node.loc.start.line);},
    StringLiteral(path){if(ownedString(path))add(path.node.value,path.node.loc.start.line);},
    TemplateLiteral(path){if(ownedString(path))add(path.node.quasis.map((part,index)=>part.value.cooked+(index<path.node.expressions.length?`{{v${index}}}`:'')).join(''),path.node.loc.start.line);},
    ArrayExpression(path){
      // Declarative [transport ID, visible label] option/tab pairs.
      if(path.node.elements.length!==2||!t.isStringLiteral(path.node.elements[0])||!t.isStringLiteral(path.node.elements[1]))return;
      if(!path.parentPath.isArrayExpression())return;
      add(path.node.elements[1].value,path.node.loc.start.line);
    }
  });
  return [...messages.values()];
}
export function uncoveredUiMessages(code,filename,english,neutral=[]){
  const allowed=new Set(neutral);
  return collectUiMessages(code,filename).filter(({key})=>!Object.hasOwn(english,key)&&!allowed.has(key)&&!/^\s*(?:\{\{[^}]+\}\}|[\d\s\p{P}\p{S}])+\s*$/u.test(key));
}
