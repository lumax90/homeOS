import {transformSync,types as t} from '@babel/core';

// Like Home's localization stage, this runs before React in both dev and production.
// Native JSX remains a typed declarative form authoring format; its visible renderer
// is always HomeSelect. No runtime DOM replacement, hydration patch or event interception.
export function transformControls(code,filename){
  let count=0;
  const result=transformSync(code,{filename,babelrc:false,configFile:false,sourceMaps:true,parserOpts:{plugins:['typescript','jsx']},plugins:[()=>({visitor:{
    JSXElement(path){
      const element=path.node;const open=element.openingElement;
      if(!t.isJSXIdentifier(open.name,{name:'select'}))return;
      // A persistent multi-selection mapping list is not a dropdown; keep its native
      // selectedOptions/size semantics. Unknown future event APIs fail the audit test.
      if(open.attributes.some(attribute=>t.isJSXAttribute(attribute)&&attribute.name.name==='multiple'))return;
      if(!open.attributes.some(attribute=>t.isJSXAttribute(attribute)&&['aria-label','aria-labelledby'].includes(attribute.name.name))){
        const label=path.findParent(parent=>parent.isJSXElement()&&t.isJSXIdentifier(parent.node.openingElement.name,{name:'label'}));
        const span=label?.node.children.find(child=>t.isJSXElement(child)&&t.isJSXIdentifier(child.openingElement.name,{name:'span'}));
        if(!span)throw path.buildCodeFrameError('A Home single-choice control needs an accessible label.');
        const pieces=span.children.filter(child=>!t.isJSXText(child)||child.value.trim()).map(child=>t.isJSXText(child)?t.stringLiteral(child.value.replace(/\s+/g,' ').trim()):t.isJSXExpressionContainer(child)?t.cloneNode(child.expression,true):null);
        if(!pieces.length||pieces.some(piece=>!piece||t.isJSXEmptyExpression(piece)))throw path.buildCodeFrameError('Use an explicit aria-label for this choice control.');
        const text=pieces.reduce((result,piece)=>result?t.binaryExpression('+',t.binaryExpression('+',result,t.stringLiteral(' ')),piece):piece,null);
        open.attributes.push(t.jsxAttribute(t.jsxIdentifier('aria-label'),t.jsxExpressionContainer(text)));
      }
      open.name=t.jsxIdentifier('__HomeSelect');if(element.closingElement)element.closingElement.name=t.jsxIdentifier('__HomeSelect');count++;
    },
    Program:{exit(path){if(count)path.unshiftContainer('body',t.importDeclaration([t.importSpecifier(t.identifier('__HomeSelect'),t.identifier('HomeSelect'))],t.stringLiteral('/src/components/HomeSelect.tsx')));}}
  }})]});
  return{code:result.code,map:result.map,count};
}
export function uiControls(){
  return{name:'home-ui-controls',enforce:'pre',transform(code,id){
    const file=id.split('?')[0].replaceAll('\\','/');
    if(!/\/src\/.*\.tsx$/.test(file)||!/<select\b/.test(code))return;
    return transformControls(code,file);
  }};
}
