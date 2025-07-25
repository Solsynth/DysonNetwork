import{A as e,B as t,C as n,D as r,E as i,F as a,G as o,b as s,c,d as l,e as u,f as d,g as f,h as p,i as m,j as h,k as g,l as _,m as v,n as y,o as b,p as x,q as S,r as C,s as w,t as T,u as E,v as D,w as O,x as k,y as A,z as j}from"./index.js";function M(e){return Object.keys(e)}var N=S([T(`card`,`
 font-size: var(--n-font-size);
 line-height: var(--n-line-height);
 display: flex;
 flex-direction: column;
 width: 100%;
 box-sizing: border-box;
 position: relative;
 border-radius: var(--n-border-radius);
 background-color: var(--n-color);
 color: var(--n-text-color);
 word-break: break-word;
 transition: 
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier),
 box-shadow .3s var(--n-bezier),
 border-color .3s var(--n-bezier);
 `,[v({background:`var(--n-color-modal)`}),C(`hoverable`,[S(`&:hover`,`box-shadow: var(--n-box-shadow);`)]),C(`content-segmented`,[S(`>`,[x(`content`,{paddingTop:`var(--n-padding-bottom)`})])]),C(`content-soft-segmented`,[S(`>`,[x(`content`,`
 margin: 0 var(--n-padding-left);
 padding: var(--n-padding-bottom) 0;
 `)])]),C(`footer-segmented`,[S(`>`,[x(`footer`,{paddingTop:`var(--n-padding-bottom)`})])]),C(`footer-soft-segmented`,[S(`>`,[x(`footer`,`
 padding: var(--n-padding-bottom) 0;
 margin: 0 var(--n-padding-left);
 `)])]),S(`>`,[T(`card-header`,`
 box-sizing: border-box;
 display: flex;
 align-items: center;
 font-size: var(--n-title-font-size);
 padding:
 var(--n-padding-top)
 var(--n-padding-left)
 var(--n-padding-bottom)
 var(--n-padding-left);
 `,[x(`main`,`
 font-weight: var(--n-title-font-weight);
 transition: color .3s var(--n-bezier);
 flex: 1;
 min-width: 0;
 color: var(--n-title-text-color);
 `),x(`extra`,`
 display: flex;
 align-items: center;
 font-size: var(--n-font-size);
 font-weight: 400;
 transition: color .3s var(--n-bezier);
 color: var(--n-text-color);
 `),x(`close`,`
 margin: 0 0 0 8px;
 transition:
 background-color .3s var(--n-bezier),
 color .3s var(--n-bezier);
 `)]),x(`action`,`
 box-sizing: border-box;
 transition:
 background-color .3s var(--n-bezier),
 border-color .3s var(--n-bezier);
 background-clip: padding-box;
 background-color: var(--n-action-color);
 `),x(`content`,`flex: 1; min-width: 0;`),x(`content, footer`,`
 box-sizing: border-box;
 padding: 0 var(--n-padding-left) var(--n-padding-bottom) var(--n-padding-left);
 font-size: var(--n-font-size);
 `,[S(`&:first-child`,{paddingTop:`var(--n-padding-bottom)`})]),x(`action`,`
 background-color: var(--n-action-color);
 padding: var(--n-padding-bottom) var(--n-padding-left);
 border-bottom-left-radius: var(--n-border-radius);
 border-bottom-right-radius: var(--n-border-radius);
 `)]),T(`card-cover`,`
 overflow: hidden;
 width: 100%;
 border-radius: var(--n-border-radius) var(--n-border-radius) 0 0;
 `,[S(`img`,`
 display: block;
 width: 100%;
 `)]),C(`bordered`,`
 border: 1px solid var(--n-border-color);
 `,[S(`&:target`,`border-color: var(--n-color-target);`)]),C(`action-segmented`,[S(`>`,[x(`action`,[S(`&:not(:first-child)`,{borderTop:`1px solid var(--n-border-color)`})])])]),C(`content-segmented, content-soft-segmented`,[S(`>`,[x(`content`,{transition:`border-color 0.3s var(--n-bezier)`},[S(`&:not(:first-child)`,{borderTop:`1px solid var(--n-border-color)`})])])]),C(`footer-segmented, footer-soft-segmented`,[S(`>`,[x(`footer`,{transition:`border-color 0.3s var(--n-bezier)`},[S(`&:not(:first-child)`,{borderTop:`1px solid var(--n-border-color)`})])])]),C(`embedded`,`
 background-color: var(--n-color-embedded);
 `)]),b(T(`card`,`
 background: var(--n-color-modal);
 `,[C(`embedded`,`
 background-color: var(--n-color-embedded-modal);
 `)])),y(T(`card`,`
 background: var(--n-color-popover);
 `,[C(`embedded`,`
 background-color: var(--n-color-embedded-popover);
 `)]))]);const P={title:[String,Function],contentClass:String,contentStyle:[Object,String],headerClass:String,headerStyle:[Object,String],headerExtraClass:String,headerExtraStyle:[Object,String],footerClass:String,footerStyle:[Object,String],embedded:Boolean,segmented:{type:[Boolean,Object],default:!1},size:{type:String,default:`medium`},bordered:{type:Boolean,default:!0},closable:Boolean,hoverable:Boolean,role:String,onClose:[Function,Array],tag:{type:String,default:`div`},cover:Function,content:[String,Function],footer:Function,action:Function,headerExtra:Function},F=M(P),I=Object.assign(Object.assign({},u.props),P);var L=A({name:`Card`,props:I,slots:Object,setup(e){let t=()=>{let{onClose:t}=e;t&&g(t)},{inlineThemeDisabled:n,mergedClsPrefixRef:r,mergedRtlRef:i}=p(e),a=u(`Card`,`-card`,N,c,e,r),o=d(`Card`,i,r),s=O(()=>{let{size:t}=e,{self:{color:n,colorModal:r,colorTarget:i,textColor:o,titleTextColor:s,titleFontWeight:c,borderColor:l,actionColor:u,borderRadius:d,lineHeight:f,closeIconColor:p,closeIconColorHover:m,closeIconColorPressed:h,closeColorHover:g,closeColorPressed:v,closeBorderRadius:y,closeIconSize:b,closeSize:x,boxShadow:S,colorPopover:C,colorEmbedded:T,colorEmbeddedModal:E,colorEmbeddedPopover:D,[w(`padding`,t)]:O,[w(`fontSize`,t)]:k,[w(`titleFontSize`,t)]:A},common:{cubicBezierEaseInOut:j}}=a.value,{top:M,left:N,bottom:P}=_(O);return{"--n-bezier":j,"--n-border-radius":d,"--n-color":n,"--n-color-modal":r,"--n-color-popover":C,"--n-color-embedded":T,"--n-color-embedded-modal":E,"--n-color-embedded-popover":D,"--n-color-target":i,"--n-text-color":o,"--n-line-height":f,"--n-action-color":u,"--n-title-text-color":s,"--n-title-font-weight":c,"--n-close-icon-color":p,"--n-close-icon-color-hover":m,"--n-close-icon-color-pressed":h,"--n-close-color-hover":g,"--n-close-color-pressed":v,"--n-border-color":l,"--n-box-shadow":S,"--n-padding-top":M,"--n-padding-bottom":P,"--n-padding-left":N,"--n-font-size":k,"--n-title-font-size":A,"--n-close-size":x,"--n-close-icon-size":b,"--n-close-border-radius":y}}),l=n?f(`card`,O(()=>e.size[0]),s,e):void 0;return{rtlEnabled:o,mergedClsPrefix:r,mergedTheme:a,handleCloseClick:t,cssVars:n?void 0:s,themeClass:l?.themeClass,onRender:l?.onRender}},render(){let{segmented:e,bordered:t,hoverable:n,mergedClsPrefix:r,rtlEnabled:i,onRender:a,embedded:o,tag:s,$slots:c}=this;return a?.(),j(s,{class:[`${r}-card`,this.themeClass,o&&`${r}-card--embedded`,{[`${r}-card--rtl`]:i,[`${r}-card--content${typeof e!=`boolean`&&e.content===`soft`?`-soft`:``}-segmented`]:e===!0||e!==!1&&e.content,[`${r}-card--footer${typeof e!=`boolean`&&e.footer===`soft`?`-soft`:``}-segmented`]:e===!0||e!==!1&&e.footer,[`${r}-card--action-segmented`]:e===!0||e!==!1&&e.action,[`${r}-card--bordered`]:t,[`${r}-card--hoverable`]:n}],style:this.cssVars,role:this.role},h(c.cover,e=>{let t=this.cover?m([this.cover()]):e;return t&&j(`div`,{class:`${r}-card-cover`,role:`none`},t)}),h(c.header,e=>{let{title:t}=this,n=t?m(typeof t==`function`?[t()]:[t]):e;return n||this.closable?j(`div`,{class:[`${r}-card-header`,this.headerClass],style:this.headerStyle,role:`heading`},j(`div`,{class:`${r}-card-header__main`,role:`heading`},n),h(c[`header-extra`],e=>{let t=this.headerExtra?m([this.headerExtra()]):e;return t&&j(`div`,{class:[`${r}-card-header__extra`,this.headerExtraClass],style:this.headerExtraStyle},t)}),this.closable&&j(l,{clsPrefix:r,class:`${r}-card-header__close`,onClick:this.handleCloseClick,absolute:!0})):null}),h(c.default,e=>{let{content:t}=this,n=t?m(typeof t==`function`?[t()]:[t]):e;return n&&j(`div`,{class:[`${r}-card__content`,this.contentClass],style:this.contentStyle,role:`none`},n)}),h(c.footer,e=>{let t=this.footer?m([this.footer()]):e;return t&&j(`div`,{class:[`${r}-card__footer`,this.footerClass],style:this.footerStyle,role:`none`},t)}),h(c.action,e=>{let t=this.action?m([this.action()]):e;return t&&j(`div`,{class:`${r}-card__action`,role:`none`},t)}))}});const R={class:`h-full relative flex items-center justify-center`},z={class:`mt-4 opacity-75 text-xs`},B={key:0},V={key:1};var H=A({__name:`index`,setup(s){let c=i(null);async function l(){let e=await fetch(`/api/version`);c.value=await e.json()}return r(()=>l()),(r,i)=>(E(),n(`section`,R,[k(a(L),{class:`max-w-lg`,title:`About`},{default:t(()=>[i[0]||=D(`p`,null,[e(`Welcome to the `),D(`b`,null,`Solar Drive`)],-1),i[1]||=D(`p`,null,` We help you upload, collect, and share files with ease in mind. `,-1),D(`p`,z,[c.value==null?(E(),n(`span`,B,`Loading...`)):(E(),n(`span`,V,` v`+o(c.value.version)+` @ `+o(c.value.commit.substring(0,6))+` `+o(c.value.updatedAt),1))])]),_:1,__:[0,1]})]))}}),U=s(H,[[`__scopeId`,`data-v-55d8ffbb`]]);export{U as default};