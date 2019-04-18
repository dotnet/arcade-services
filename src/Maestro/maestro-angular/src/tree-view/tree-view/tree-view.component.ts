import { Component, OnInit, Input, ContentChild, TemplateRef } from '@angular/core';
import { TreeNode } from '../definitions';

@Component({
  selector: 'tree-view',
  templateUrl: './tree-view.component.html',
  styleUrls: ['./tree-view.component.scss']
})
export class TreeViewComponent implements OnInit {
  @ContentChild(TemplateRef) public nodeTemplate?: TemplateRef<any>;
  @Input() public data?: TreeNode[];

  constructor() { }

  ngOnInit() {
  }

}
